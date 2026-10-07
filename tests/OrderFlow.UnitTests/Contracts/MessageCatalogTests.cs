using System.Reflection;
using OrderFlow.Contracts;
using OrderFlow.Contracts.Demo;

namespace OrderFlow.UnitTests.Contracts;

public class MessageCatalogTests
{
    private static readonly Type[] AllMessages = typeof(OrderCreated).Assembly.GetTypes()
        .Where(t => t.GetCustomAttribute<MessageTypeAttribute>() is not null)
        .ToArray();

    [Fact]
    public void Routing_keys_are_unique_and_round_trip_to_their_types()
    {
        Assert.NotEmpty(AllMessages);
        foreach (var type in AllMessages)
        {
            var key = MessageCatalog.RoutingKeyOf(type);
            Assert.True(MessageCatalog.TryGetType(key, out var back));
            Assert.Equal(type, back);
        }

        Assert.Equal(AllMessages.Length, MessageCatalog.RoutingKeys.Count);
    }

    [Fact]
    public void Routing_keys_use_the_lowercase_dotted_convention()
    {
        Assert.All(MessageCatalog.RoutingKeys, key => Assert.Matches("^[a-z]+(\\.[a-z-]+)+$", key));
    }

    [Fact]
    public void A_type_without_the_attribute_is_not_a_message()
    {
        Assert.Throws<ArgumentException>(MessageCatalog.RoutingKeyOf<OrderLine>);
        Assert.False(MessageCatalog.TryGetType("does.not-exist", out _));
    }

    [Fact]
    public void Every_queue_binds_only_known_routing_keys_and_only_leaf_events_have_no_consumer()
    {
        foreach (var queue in Topology.All)
        {
            Assert.All(queue.RoutingKeys, key => Assert.True(MessageCatalog.TryGetType(key, out _), $"{queue.Name} binds unknown key {key}"));
        }

        var consumed = Topology.All.SelectMany(q => q.RoutingKeys).ToHashSet();
        var unconsumed = MessageCatalog.RoutingKeys.Where(key => !consumed.Contains(key)).ToList();

        // The rule: a message that requires a consumer must be bound somewhere; only pure announcements (here
        // OrderConfirmed: a notification service could listen later) may have no queue.
        var announcements = AllMessages
            .Where(type => type.GetCustomAttribute<MessageTypeAttribute>()!.RequiresConsumer is false)
            .Select(MessageCatalog.RoutingKeyOf)
            .ToList();
        Assert.Equal(announcements.Order().ToList(), unconsumed.Order().ToList());
        Assert.Equal([MessageCatalog.RoutingKeyOf<OrderConfirmed>()], announcements);
        Assert.False(MessageCatalog.RequiresConsumer<OrderConfirmed>());
        Assert.True(MessageCatalog.RequiresConsumer<OrderCreated>());
    }

    [Fact]
    public void Demo_catalog_ids_are_unique_and_prices_positive()
    {
        var products = DemoCatalog.Products;

        Assert.Equal(products.Count, products.Select(p => p.Id).Distinct().Count());
        Assert.All(products, p =>
        {
            Assert.True(p.Price > 0);
            Assert.True(p.Stock > 0);
        });
    }
}
