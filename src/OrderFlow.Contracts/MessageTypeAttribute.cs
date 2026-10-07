namespace OrderFlow.Contracts;

/// <summary>
/// Declares the routing key a message is published with. The key is the message's identity on the wire:
/// renaming it is a breaking change, so contracts change additively (new optional fields) or get a new key.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MessageTypeAttribute(string routingKey) : Attribute
{
    public string RoutingKey { get; } = routingKey;
}
