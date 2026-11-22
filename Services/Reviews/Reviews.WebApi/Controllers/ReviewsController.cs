using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Reviews.Application.Commands;
using Reviews.Application.Common;
using Reviews.Application.Queries;
using Reviews.WebApi.Contracts;

namespace Reviews.WebApi.Controllers;

/// <summary>Контролер працює лише через ISender (MediatR) — жодної бізнес-логіки й доступу до БД.</summary>
[ApiController]
[Route("api/reviews")]
[Produces("application/json")]
public sealed class ReviewsController(ISender sender, IMapper mapper) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedList<ReviewListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedList<ReviewListItemDto>>> GetList(
        [FromQuery] long? productId, [FromQuery] int? minRating, [FromQuery] string? authorId, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string sortBy = "newest", CancellationToken ct = default) =>
        Ok(await sender.Send(new GetReviewsListQuery(productId, minRating, authorId, search, page, pageSize, sortBy), ct));

    [HttpGet("{id}")]
    [ProducesResponseType<ReviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReviewDto>> GetById(string id, CancellationToken ct) =>
        Ok(await sender.Send(new GetReviewByIdQuery(id), ct));

    [HttpGet("products/{productId:long}/rating")]
    public async Task<ActionResult<ProductRatingDto>> GetProductRating(long productId, CancellationToken ct) =>
        Ok(await sender.Send(new GetProductRatingQuery(productId), ct));

    [HttpPost]
    [ProducesResponseType<ReviewDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReviewDto>> Create([FromBody] CreateReviewRequest request, CancellationToken ct)
    {
        var id = await sender.Send(mapper.Map<CreateReviewCommand>(request), ct);
        var created = await sender.Send(new GetReviewByIdQuery(id), ct);
        return CreatedAtAction(nameof(GetById), new { id }, created); // Location з рядковим ObjectId
    }

    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateReviewRequest request, CancellationToken ct)
    {
        await sender.Send(mapper.Map<UpdateReviewCommand>(request) with { Id = id }, ct);
        return NoContent();
    }

    [HttpPost("{id}/comments")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> AddComment(string id, [FromBody] AddCommentRequest request, CancellationToken ct)
    {
        var commentId = await sender.Send(mapper.Map<AddCommentCommand>(request) with { ReviewId = id }, ct);
        return CreatedAtAction(nameof(GetById), new { id }, new { id = commentId });
    }

    [HttpPost("{id}/helpful")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkHelpful(string id, CancellationToken ct)
    {
        await sender.Send(new MarkReviewHelpfulCommand(id), ct);
        return NoContent();
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await sender.Send(new DeleteReviewCommand(id), ct);
        return NoContent();
    }
}
