using MediatR;
using Microsoft.AspNetCore.Mvc;
using SampleErp.Application;
using SampleErp.Common;

namespace SampleErp.Api.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    private readonly ISender _sender;

    public OrdersController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost]
    public async Task<ActionResult<Guid>> Create([FromBody] CreateOrderRequest request)
    {
        var orderId = await _sender.Send(new CreateOrderCommand { Total = new Money(request.Amount, request.Currency) });
        return Ok(orderId);
    }
}

public sealed record CreateOrderRequest(decimal Amount, string Currency);
