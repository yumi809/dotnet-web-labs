using Microsoft.AspNetCore.Mvc;
using Orders.Bll.Dto;
using Orders.Bll.Services;

namespace Orders.Api.Controllers;

[ApiController]
[Route("api/orders/customers")]
[Produces("application/json")]
public sealed class CustomersController(ICustomerService customers) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CustomerDto>>> GetAll(CancellationToken ct) =>
        Ok(await customers.GetAllAsync(ct));

    [HttpGet("{id:long}")]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerDto>> GetById(long id, CancellationToken ct) =>
        Ok(await customers.GetAsync(id, ct));

    [HttpGet("{id:long}/stats")]
    public async Task<ActionResult<CustomerStatsDto>> GetStats(long id, CancellationToken ct) =>
        Ok(await customers.GetStatsAsync(id, ct));

    [HttpPost]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerDto>> Create([FromBody] CreateCustomerDto dto, CancellationToken ct)
    {
        var created = await customers.CreateAsync(dto, User.Identity?.Name ?? "anonymous", ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Локальний довідник товарів (копія з Каталогу).</summary>
    [HttpGet("/api/orders/products")]
    public async Task<ActionResult<IReadOnlyList<ProductInfoDto>>> GetProducts(CancellationToken ct) =>
        Ok(await customers.GetProductDirectoryAsync(ct));
}
