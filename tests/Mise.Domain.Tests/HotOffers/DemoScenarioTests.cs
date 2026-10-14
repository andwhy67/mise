using Mise.Domain.Catalog;
using Mise.Domain.HotOffers;
using Mise.Domain.Menu;
using Mise.Infrastructure.Seed;
using Xunit.Abstractions;
using static Mise.Infrastructure.Seed.SeedData;

namespace Mise.Domain.Tests.HotOffers;

/// <summary>
/// «Готово, когда» фазы 5: на демо-наполнении топ-1 очевидно правильный. Рецепты — из SeedData, склад — из DemoStockSeeder,
/// без БД: скорер видит ровно то, что увидит API сразу после POST /demo/seed-stock.
/// </summary>
public class DemoScenarioTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(HotOfferScorerV1.Name)]
    [InlineData(HotOfferScorerV2.Name)]
    public void Demo_stock_puts_grilled_salmon_with_spinach_first(string version)
    {
        IHotOfferScorer scorer = version == HotOfferScorerV1.Name ? new HotOfferScorerV1() : new HotOfferScorerV2();

        var offers = HotOfferSelector.Select(scorer.Score(DemoInput(), HotOfferParameters.Default), [], HotOfferParameters.Default);
        foreach (var offer in offers)
            output.WriteLine($"{offer.Score,5} {offer.DishName} | {HotOfferExplainer.Explain(offer).Headline}");

        // Лосось на гриле: шпинат (4 ч), лосось (12 ч) и сливки (20 ч) горят в одном блюде.
        Assert.Equal(Dishes.GrilledSalmon, offers[0].DishId);
        Assert.Equal(Products.Spinach, offers[0].Reasons[0].ProductId);
        // Все рекомендации — про горящие лосось, шпинат или сливки; блюд без них нет.
        var burning = new[] { Products.Salmon, Products.Spinach, Products.Cream };
        Assert.All(offers, o => Assert.Contains(o.Reasons, r => burning.Contains(r.ProductId)));
        Assert.DoesNotContain(offers, o => o.DishId == Dishes.CaesarShrimp); // креветок нет — блюдо в стопе
        Assert.Equal(HotOfferParameters.Default.TopN, offers.Count);
    }

    private static HotOfferInput DemoInput()
    {
        var products = SeedData.CreateProducts().ToDictionary(p => p.Id);
        var batches = DemoStockSeeder.BuildSupplies("DEMO", Now)
            .SelectMany(s => s.Items.Select(i =>
            {
                var receivedAt = s.CreatedAt ?? Now;
                var shelfLife = i.ShelfLifeHours ?? products[i.ProductId].DefaultShelfLifeHours!.Value;
                return new HotOfferBatch(Guid.NewGuid(), i.ProductId, receivedAt, receivedAt.AddHours(shelfLife), i.Quantity);
            }))
            .ToList();
        var usable = batches.GroupBy(b => b.ProductId).ToDictionary(g => g.Key, g => g.Sum(b => b.RemainingQty));

        return new HotOfferInput(
            products.Values.Select(p => new HotOfferProduct(p.Id, p.Name, p.Unit, p.DefaultShelfLifeHours, p.MinStockQty, 0m)).ToList(),
            batches,
            SeedData.CreateDishes().Select(d => ToDish(d, usable)).ToList(),
            Now);
    }

    private static HotOfferDish ToDish(Dish dish, IReadOnlyDictionary<Guid, decimal> usable)
    {
        var availability = MenuAvailability.Calculate(dish, usable);
        return new HotOfferDish(dish.Id, dish.Name, availability.InStopList ? 0 : availability.MaxPortions,
            dish.Ingredients.Select(i => new HotOfferIngredient(i.ProductId, i.Quantity)).ToList());
    }
}
