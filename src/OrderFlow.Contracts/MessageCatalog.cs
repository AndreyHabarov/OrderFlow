using System.Reflection;

namespace OrderFlow.Contracts;

/// <summary>Maps message CLR types to routing keys and back, built once from the <see cref="MessageTypeAttribute"/> declarations.</summary>
public static class MessageCatalog
{
    private static readonly Dictionary<Type, string> KeysByType = typeof(MessageCatalog).Assembly
        .GetTypes()
        .Select(type => (type, attribute: type.GetCustomAttribute<MessageTypeAttribute>()))
        .Where(pair => pair.attribute is not null)
        .ToDictionary(pair => pair.type, pair => pair.attribute!.RoutingKey);

    private static readonly Dictionary<string, Type> TypesByKey = KeysByType.ToDictionary(pair => pair.Value, pair => pair.Key);

    private static readonly Dictionary<Type, bool> RequiresConsumerByType = typeof(MessageCatalog).Assembly
        .GetTypes()
        .Select(type => (type, attribute: type.GetCustomAttribute<MessageTypeAttribute>()))
        .Where(pair => pair.attribute is not null)
        .ToDictionary(pair => pair.type, pair => pair.attribute!.RequiresConsumer);

    public static IReadOnlyCollection<string> RoutingKeys => TypesByKey.Keys;

    /// <summary>Whether publishing this message must fail when no queue is bound for it (see <see cref="MessageTypeAttribute.RequiresConsumer"/>).</summary>
    public static bool RequiresConsumer<T>() =>
        RequiresConsumerByType.TryGetValue(typeof(T), out var required)
            ? required
            : throw new ArgumentException($"{typeof(T).Name} is not a message: it has no [MessageType] attribute.");

    public static string RoutingKeyOf(Type messageType) =>
        KeysByType.TryGetValue(messageType, out var key)
            ? key
            : throw new ArgumentException($"{messageType.Name} is not a message: it has no [MessageType] attribute.", nameof(messageType));

    public static string RoutingKeyOf<T>() => RoutingKeyOf(typeof(T));

    public static bool TryGetType(string routingKey, out Type messageType)
    {
        var found = TypesByKey.TryGetValue(routingKey, out var type);
        messageType = type!;
        return found;
    }
}
