using MediatR;
using SampleErp.Domain;

namespace SampleErp.Application;

public sealed class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Guid>
{
    private readonly IOrderRepository _orderRepository;

    public CreateOrderCommandHandler(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public async Task<Guid> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        var order = new Order { Id = Guid.NewGuid(), Total = request.Total };
        await _orderRepository.AddAsync(order, cancellationToken);
        return order.Id;
    }
}
