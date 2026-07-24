using Microsoft.EntityFrameworkCore;
using SampleErp.Domain;

namespace SampleErp.Infrastructure;

public sealed class OrderRepository : IOrderRepository
{
    private readonly AppDbContext _dbContext;

    public OrderRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Order?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _dbContext.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task AddAsync(Order order, CancellationToken ct = default)
    {
        await _dbContext.Orders.AddAsync(order, ct);
        await _dbContext.SaveChangesAsync(ct);
    }
}
