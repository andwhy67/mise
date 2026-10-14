using Mise.Application.Menu;
using Mise.Application.Realtime;

namespace Mise.Application.Tests;

public class RealtimeChangeSetTests
{
    [Fact]
    public void New_change_set_is_empty()
    {
        Assert.True(new RealtimeChangeSet().IsEmpty);
    }

    [Fact]
    public void Repeated_changes_collapse()
    {
        var product = Guid.NewGuid();
        var changes = new RealtimeChangeSet();

        changes.Products.UnionWith([product, product]);
        changes.Products.Add(product);

        Assert.Single(changes.Products);
        Assert.False(changes.IsEmpty);
    }

    [Fact]
    public void Stop_list_keeps_the_last_state_of_each_dish()
    {
        var flapping = Guid.NewGuid();
        var stays = Guid.NewGuid();
        var changes = new RealtimeChangeSet();

        changes.AddStopListChanges(new StopListChanges([flapping, stays], []));
        changes.AddStopListChanges(new StopListChanges([], [flapping]));

        Assert.Equal([stays], changes.StopListAdded);
        Assert.Equal([flapping], changes.StopListRemoved);

        changes.AddStopListChanges(new StopListChanges([flapping], []));
        Assert.Contains(flapping, changes.StopListAdded);
        Assert.Empty(changes.StopListRemoved);
    }

    [Fact]
    public void Status_change_of_an_order_created_in_the_same_window_is_not_sent_twice()
    {
        var created = Guid.NewGuid();
        var older = Guid.NewGuid();
        var changes = new RealtimeChangeSet();

        changes.OrdersCreated.Add(created);
        changes.AddOrderUpdated(created);
        changes.AddOrderUpdated(older);

        Assert.Equal([older], changes.OrdersUpdated);
    }
}
