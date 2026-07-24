using MediatR;
using SampleErp.Common;

namespace SampleErp.Application;

public sealed class CreateOrderCommand : IRequest<Guid>
{
    public required Money Total { get; init; }
}
