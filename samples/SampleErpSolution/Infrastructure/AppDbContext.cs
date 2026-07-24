using Microsoft.EntityFrameworkCore;
using SampleErp.Domain;

namespace SampleErp.Infrastructure;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();
}
