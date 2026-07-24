using SampleErp.Application;
using SampleErp.Common;
using SampleErp.Domain;

namespace SampleErp.Tests;

public class CreateOrderCommandHandlerTests
{
    private sealed class FakeOrderRepository : IOrderRepository
    {
        public Order? Saved { get; private set; }

        public Task<Order?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Saved);

        public Task AddAsync(Order order, CancellationToken ct = default)
        {
            Saved = order;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Handle_SavesOrder_WithRequestedTotal()
    {
        var repository = new FakeOrderRepository();
        var handler = new CreateOrderCommandHandler(repository);

        var orderId = await handler.Handle(new CreateOrderCommand { Total = new Money(42m, "USD") }, CancellationToken.None);

        Assert.Equal(orderId, repository.Saved!.Id);
        Assert.Equal(42m, repository.Saved.Total.Amount);
    }
}
