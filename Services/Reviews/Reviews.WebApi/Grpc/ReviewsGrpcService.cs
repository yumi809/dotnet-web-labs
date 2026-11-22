using FluentValidation;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MediatR;
using Reviews.Application.Common;
using Reviews.Application.Queries;
using Reviews.Grpc.V1;

namespace Reviews.WebApi.Grpc;

/// <summary>gRPC-адаптер над CQRS-запитами (ISender). Помилки → NotFound / InvalidArgument.</summary>
public sealed class ReviewsGrpcService(ISender sender) : ReviewsService.ReviewsServiceBase
{
    public override async Task<ProductRatingReply> GetProductRating(ProductRatingRequest request, ServerCallContext context)
    {
        if (request.ProductId <= 0) throw new RpcException(new Status(StatusCode.InvalidArgument, "product_id must be positive"));
        try
        {
            var rating = await sender.Send(new GetProductRatingQuery(request.ProductId), context.CancellationToken);
            return new ProductRatingReply
            {
                ProductId = rating.ProductId, ProductName = rating.ProductName,
                ReviewsCount = rating.ReviewsCount, Average = rating.Average
            };
        }
        catch (NotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
    }

    public override async Task<LatestReviewsReply> GetLatestReviews(LatestReviewsRequest request, ServerCallContext context)
    {
        try
        {
            var page = await sender.Send(new GetReviewsListQuery(request.ProductId, null, null, null, 1, request.Count == 0 ? 3 : request.Count),
                context.CancellationToken);
            var reply = new LatestReviewsReply { TotalCount = page.TotalCount };
            reply.Reviews.AddRange(page.Items.Select(r => new ReviewSummary
            {
                Id = r.Id, AuthorName = r.AuthorName, Rating = r.Rating, Title = r.Title,
                HelpfulVotes = r.HelpfulVotes, CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc))
            }));
            return reply;
        }
        catch (ValidationException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, string.Join("; ", ex.Errors.Select(e => e.ErrorMessage))));
        }
    }
}
