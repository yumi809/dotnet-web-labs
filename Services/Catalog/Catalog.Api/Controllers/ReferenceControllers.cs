using Catalog.Bll.Dto;
using Catalog.Bll.Services;
using Microsoft.AspNetCore.Mvc;

namespace Catalog.Api.Controllers;

[ApiController]
[Route("api/catalog/publishers")]
[Produces("application/json")]
public sealed class PublishersController(IPublisherService publishers) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PublisherDto>>> GetAll(CancellationToken ct) => Ok(await publishers.GetAllAsync(ct));

    [HttpGet("{id:int}")]
    [ProducesResponseType<PublisherWithProductsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PublisherWithProductsDto>> GetById(int id, CancellationToken ct) =>
        Ok(await publishers.GetWithProductsAsync(id, ct));

    [HttpPost]
    [ProducesResponseType<PublisherDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PublisherDto>> Create([FromBody] CreatePublisherDto dto, CancellationToken ct)
    {
        var created = await publishers.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }
}

[ApiController]
[Route("api/catalog/categories")]
[Produces("application/json")]
public sealed class CategoriesController(ICategoryService categories) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> GetAll(CancellationToken ct) => Ok(await categories.GetAllAsync(ct));

    /// <summary>Новинки категорії за N днів (запит M:N через поле AddedAt проміжної сутності).</summary>
    [HttpGet("{id:int}/new-arrivals")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<CategoryProductDto>>> GetNewArrivals(int id, [FromQuery] int days = 30, CancellationToken ct = default) =>
        Ok(await categories.GetNewArrivalsAsync(id, days, ct));
}
