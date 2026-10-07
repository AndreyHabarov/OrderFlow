using OrderFlow.Domain.Orders;

namespace OrderFlow.Application.Abstractions;

/// <summary>
/// Announces order facts to the other services. The implementation lives in Infrastructure, so Application never
/// touches the broker. Publishing happens AFTER the database commit (ADR 0006, known gap 1: stage 3 replaces this
/// with a transactional outbox).
/// </summary>
public interface IOrderEventPublisher
{
    Task OrderCreatedAsync(Order order, CancellationToken cancellationToken);

    Task OrderConfirmedAsync(Order order, CancellationToken cancellationToken);
}
