using Catalog.Bll.Dto;
using Catalog.Bll.Services;
using Catalog.Domain.Exceptions;
using Catalog.Grpc.V1;
using Grpc.Core;

namespace Catalog.Api.Grpc;

/// <summary>
/// gRPC-адаптер над BLL: лише валідація вхідних даних, виклик сервісу і перетворення помилок
/// на коректні статуси (NotFound / InvalidArgument), а не Internal.
/// </summary>
[Microsoft.AspNetCore.Authorization.Authorize(Policy = "catalog.read")]   // токен IdentityServer (Aggregator, Client Credentials)
public sealed class CatalogGrpcService(IProductService products, ILogger<CatalogGrpcService> logger) : CatalogService.CatalogServiceBase
{
    private const int MaxBatch = 100;

    public override async Task<ProductReply> GetProduct(GetProductRequest request, ServerCallContext context)
    {
        if (request.Id <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "id must be positive"));
        try
        {
            return Map(await products.GetAsync(request.Id, context.CancellationToken));
        }
        catch (NotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
    }

    public override async Task<ProductsReply> GetProducts(GetProductsRequest request, ServerCallContext context)
    {
        if (request.Ids.Count == 0 || request.Ids.Count > MaxBatch)
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"ids must contain 1..{MaxBatch} items"));
        if (request.Ids.Any(id => id <= 0))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "all ids must be positive"));

        var reply = new ProductsReply();
        foreach (var id in request.Ids.Distinct())
        {
            try
            {
                reply.Products.Add(Map(await products.GetAsync(id, context.CancellationToken)));
            }
            catch (NotFoundException)
            {
                reply.NotFoundIds.Add(id);
            }
        }
        logger.LogDebug("GetProducts returned {Found} products, {Missing} not found", reply.Products.Count, reply.NotFoundIds.Count);
        return reply;
    }

    private static ProductReply Map(ProductDto p)
    {
        var reply = new ProductReply
        {
            Id = p.Id,
            Sku = p.Sku,
            Name = p.Name,
            Author = p.Author,
            PriceCents = (long)(p.Price * 100),
            StockQuantity = p.StockQuantity,
            IsActive = p.IsActive,
            PublisherName = p.PublisherName
        };
        reply.Categories.AddRange(p.Categories.Select(c => new CategoryRef { Id = c.Id, Name = c.Name }));
        return reply;
    }
}
