using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orders.Bll.Dto;
using Orders.Bll.Services;
using Platform.ServiceDefaults.Authorization;

namespace Orders.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/orders/customers")]
[Produces("application/json")]
public sealed class CustomersController(ICustomerService customers) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Orders.Read)]
    public async Task<ActionResult<IReadOnlyList<CustomerDto>>> GetAll(CancellationToken ct) =>
        Ok(await customers.GetAllAsync(ct));

    [HttpGet("{id:long}")]
    [RequirePermission(Permissions.Orders.Read)]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerDto>> GetById(long id, CancellationToken ct) =>
        Ok(await customers.GetAsync(id, ct));

    [HttpGet("{id:long}/stats")]
    [RequirePermission(Permissions.Orders.Read)]
    public async Task<ActionResult<CustomerStatsDto>> GetStats(long id, CancellationToken ct) =>
        Ok(await customers.GetStatsAsync(id, ct));

    [HttpPost]
    [RequirePermission(Permissions.Orders.Create)]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerDto>> Create([FromBody] CreateCustomerDto dto, CancellationToken ct)
    {
        var user = User.FindFirstValue("username") ?? User.FindFirstValue("preferred_username") ?? User.FindFirstValue("sub") ?? "anonymous";
        var created = await customers.CreateAsync(dto, user, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Локальний довідник товарів (копія з Каталогу).</summary>
    [HttpGet("/api/orders/products")]
    [RequirePermission(Permissions.Products.Read)]
    public async Task<ActionResult<IReadOnlyList<ProductInfoDto>>> GetProducts(CancellationToken ct) =>
        Ok(await customers.GetProductDirectoryAsync(ct));
}
