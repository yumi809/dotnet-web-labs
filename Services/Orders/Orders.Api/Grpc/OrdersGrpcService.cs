using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Orders.Bll.Services;
using Orders.Domain.Exceptions;
using Orders.Grpc.V1;

namespace Orders.Api.Grpc;

/// <summary>gRPC-адаптер над BLL Замовлень. Внутрішній виклик Aggregator: токен IdentityServer (Client Credentials) зі scope orders.read.</summary>
[Microsoft.AspNetCore.Authorization.Authorize(Policy = "orders.read")]
public sealed class OrdersGrpcService(IOrderService orders, ICustomerService customers) : OrdersService.OrdersServiceBase
{
    public override async Task<OrderReply> GetOrder(GetOrderRequest request, ServerCallContext context)
    {
        if (request.Id <= 0) throw new RpcException(new Status(StatusCode.InvalidArgument, "id must be positive"));
        try
        {
            var o = await orders.GetAsync(request.Id, context.CancellationToken);
            var reply = new OrderReply
            {
                Id = o.Id, OrderNumber = o.OrderNumber, CustomerId = o.CustomerId, Status = o.Status,
                PayableCents = (long)(o.Payable * 100),
                CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(o.CreatedAt, DateTimeKind.Utc))
            };
            reply.Items.AddRange(o.Items.Select(i => new OrderItem
            {
                ProductId = i.ProductId, ProductName = i.ProductName, UnitPriceCents = (long)(i.UnitPrice * 100), Quantity = i.Quantity
            }));
            return reply;
        }
        catch (NotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
    }

    public override async Task<CustomerReply> GetCustomer(GetCustomerRequest request, ServerCallContext context)
    {
        if (request.Id <= 0) throw new RpcException(new Status(StatusCode.InvalidArgument, "id must be positive"));
        try
        {
            var c = await customers.GetAsync(request.Id, context.CancellationToken);
            return new CustomerReply { Id = c.Id, FullName = c.FullName, Email = c.Email, City = c.ShippingCity, LoyaltyLevel = c.LoyaltyLevel };
        }
        catch (NotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
    }
}
