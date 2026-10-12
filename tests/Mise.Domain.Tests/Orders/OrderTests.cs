using Mise.Domain.Common;
using Mise.Domain.Orders;

namespace Mise.Domain.Tests.Orders;

public class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    private static Order NewOrder() => new(Guid.NewGuid(), "ORD-000001", Now);

    [Theory]
    [InlineData(OrderStatus.Created, OrderStatus.Cooking)]
    [InlineData(OrderStatus.Created, OrderStatus.Done)]
    [InlineData(OrderStatus.Cooking, OrderStatus.Done)]
    public void Forward_transitions_are_allowed(OrderStatus from, OrderStatus to)
    {
        var order = NewOrder();
        if (from != OrderStatus.Created)
            order.ChangeStatus(from, Now);

        order.ChangeStatus(to, Now.AddMinutes(5));

        Assert.Equal(to, order.Status);
        Assert.Equal(Now.AddMinutes(5), order.UpdatedAt);
    }

    [Theory]
    [InlineData(OrderStatus.Done, OrderStatus.Cooking)]
    [InlineData(OrderStatus.Cooking, OrderStatus.Created)]
    [InlineData(OrderStatus.Done, OrderStatus.Cancelled)]
    public void Backward_or_terminal_transitions_are_rejected(OrderStatus from, OrderStatus to)
    {
        var order = NewOrder();
        order.ChangeStatus(from, Now);

        var ex = Assert.Throws<DomainException>(() => order.ChangeStatus(to, Now));

        Assert.Equal(Order.Errors.InvalidTransition, ex.Code);
    }

    [Fact]
    public void Same_status_is_a_no_op()
    {
        var order = NewOrder();
        order.ChangeStatus(OrderStatus.Cooking, Now);

        order.ChangeStatus(OrderStatus.Cooking, Now.AddHours(1));

        Assert.Equal(Now, order.UpdatedAt);
    }

    [Theory]
    [InlineData(OrderStatus.Created, true)]
    [InlineData(OrderStatus.Cooking, true)]
    [InlineData(OrderStatus.Done, false)]
    [InlineData(OrderStatus.Cancelled, false)]
    public void Only_open_orders_can_be_cancelled(OrderStatus status, bool cancellable)
    {
        var order = NewOrder();
        if (status == OrderStatus.Cancelled)
            order.Cancel(Now);
        else if (status != OrderStatus.Created)
            order.ChangeStatus(status, Now);

        Assert.Equal(cancellable, order.CanBeCancelled);
    }

    [Fact]
    public void Cancel_sets_status_and_time()
    {
        var order = NewOrder();

        order.Cancel(Now.AddMinutes(3));

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(Now.AddMinutes(3), order.CancelledAt);
        Assert.Throws<DomainException>(() => order.Cancel(Now.AddMinutes(4)));
    }

    [Fact]
    public void Total_is_sum_of_price_snapshots()
    {
        var order = NewOrder();
        order.AddItem(Guid.NewGuid(), "Карбонара", 2, 690m);
        order.AddItem(Guid.NewGuid(), "Цезарь", 1, 590m);

        Assert.Equal(1970m, order.Total);
        Assert.Equal([1, 2], order.Items.Select(i => i.LineNo));
    }
}
