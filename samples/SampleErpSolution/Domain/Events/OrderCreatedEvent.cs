using MediatR;

namespace SampleErp.Domain.Events;

public sealed class OrderCreatedEvent : INotification
{
    public required Guid OrderId { get; init; }
}
