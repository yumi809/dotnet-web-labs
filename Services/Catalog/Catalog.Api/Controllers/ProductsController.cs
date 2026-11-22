using Catalog.Bll.Dto;
using Catalog.Bll.Services;
using Microsoft.AspNetCore.Mvc;

namespace Catalog.Api.Controllers;

[ApiController]
[Route("api/catalog/products")]
[Produces("application/json")]
public sealed class ProductsController(IProductService products) : ControllerBase
{
    /// <summary>Список з пагінацією, фільтрацією і сортуванням (на рівні SQL).</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<ProductListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ProductListItemDto>>> GetPaged([FromQuery] ProductQueryDto query, CancellationToken ct) =>
        Ok(await products.GetPagedAsync(query, ct));

    [HttpGet("{id:long}")]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> GetById(long id, CancellationToken ct) =>
        Ok(await products.GetAsync(id, ct));

    [HttpGet("low-stock")]
    public async Task<ActionResult<IReadOnlyList<ProductListItemDto>>> GetLowStock([FromQuery] int threshold = 5, CancellationToken ct = default) =>
        Ok(await products.GetLowStockAsync(threshold, ct));

    [HttpPost]
    [ProducesResponseType<ProductDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductDto>> Create([FromBody] CreateProductDto dto, CancellationToken ct)
    {
        var created = await products.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:long}")]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> Update(long id, [FromBody] UpdateProductDto dto, CancellationToken ct) =>
        Ok(await products.UpdateAsync(id, dto, ct));

    [HttpPost("{id:long}/stock/add")]
    public async Task<ActionResult<ProductDto>> AddStock(long id, [FromBody] StockChangeDto dto, CancellationToken ct) =>
        Ok(await products.AddStockAsync(id, dto.Quantity, ct));

    [HttpPost("{id:long}/stock/remove")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductDto>> RemoveStock(long id, [FromBody] StockChangeDto dto, CancellationToken ct) =>
        Ok(await products.RemoveStockAsync(id, dto.Quantity, ct));

    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await products.DeleteAsync(id, ct);
        return NoContent();
    }
}
