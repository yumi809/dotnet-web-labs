using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orders.Bll.Dto;
using Orders.Bll.Services;
using Platform.ServiceDefaults.Authorization;

namespace Orders.Api.Controllers;

/// <summary>
/// Тонкий контролер: маршрутизація, валідація моделі ([ApiController]) і виклик BLL.
/// Лаб. 09: [Authorize] + permission-based авторизація [RequirePermission] на КОЖНОМУ ендпоінті;
/// ім'я користувача з токена (claims) передається в бізнес-логіку (аудит CreatedBy/UpdatedBy/ChangedBy).
/// </summary>
[ApiController]
[Authorize]
[Route("api/orders")]
[Produces("application/json")]
public sealed class OrdersController(IOrderService orders) : ControllerBase
{
    private string CurrentUser =>
        User.FindFirstValue("username") ?? User.FindFirstValue("preferred_username")
        ?? User.FindFirstValue("client_id") ?? User.FindFirstValue("sub") ?? "anonymous";

    [HttpGet("{id:long}")]
    [RequirePermission(Permissions.Orders.Read)]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDto>> GetById(long id, CancellationToken ct) =>
        Ok(await orders.GetAsync(id, ct));

    [HttpGet]
    [RequirePermission(Permissions.Orders.Read)]
    [ProducesResponseType<IReadOnlyList<OrderDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OrderDto>>> GetByCustomer([FromQuery] long customerId, CancellationToken ct) =>
        Ok(await orders.GetByCustomerAsync(customerId, ct));

    [HttpPost]
    [RequirePermission(Permissions.Orders.Create)]
    [ProducesResponseType<OrderDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDto>> Create([FromBody] CreateOrderDto dto, CancellationToken ct)
    {
        var created = await orders.CreateAsync(dto, CurrentUser, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPost("{id:long}/items")]
    [RequirePermission(Permissions.Orders.Update)]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDto>> AddItem(long id, [FromBody] AddOrderItemDto dto, CancellationToken ct) =>
        Ok(await orders.AddItemAsync(id, dto, ct));

    [HttpPatch("{id:long}/status")]
    [RequirePermission(Permissions.Orders.Update)]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDto>> ChangeStatus(long id, [FromBody] ChangeOrderStatusDto dto, CancellationToken ct) =>
        Ok(await orders.ChangeStatusAsync(id, dto, CurrentUser, ct));

    [HttpPut("{id:long}/comment")]
    [RequirePermission(Permissions.Orders.Update)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateComment(long id, [FromBody] UpdateOrderCommentDto dto, CancellationToken ct)
    {
        await orders.UpdateCommentAsync(id, dto, CurrentUser, ct);
        return NoContent();
    }

    [HttpGet("{id:long}/history")]
    [RequirePermission(Permissions.Orders.Read)]
    [ProducesResponseType<IReadOnlyList<OrderStatusHistoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OrderStatusHistoryDto>>> GetHistory(long id, CancellationToken ct) =>
        Ok(await orders.GetHistoryAsync(id, ct));

    [HttpDelete("{id:long}")]
    [RequirePermission(Permissions.Orders.Delete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await orders.DeleteAsync(id, CurrentUser, ct);
        return NoContent();
    }
}
