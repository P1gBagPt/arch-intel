using SampleErp.Common;

namespace SampleErp.Domain;

public sealed class Order
{
    public Guid Id { get; init; }
    public Money Total { get; init; }
    public List<OrderItem> Items { get; init; } = [];
}

public sealed class OrderItem
{
    public Guid Id { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public int Quantity { get; init; }
}
