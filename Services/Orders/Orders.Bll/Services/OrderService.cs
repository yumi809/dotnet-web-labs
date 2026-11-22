using System.Data;
using AutoMapper;
using Microsoft.Extensions.Logging;
using Orders.Bll.Dto;
using Orders.Dal.Interfaces;
using Orders.Domain.Exceptions;
using Orders.Domain.Models;

namespace Orders.Bll.Services;

public interface IOrderService
{
    Task<OrderDto> GetAsync(long id, CancellationToken ct);
    Task<IReadOnlyList<OrderDto>> GetByCustomerAsync(long customerId, CancellationToken ct);
    Task<OrderDto> CreateAsync(CreateOrderDto dto, string user, CancellationToken ct);
    Task<OrderDto> AddItemAsync(long orderId, AddOrderItemDto dto, CancellationToken ct);
    Task<OrderDto> ChangeStatusAsync(long orderId, ChangeOrderStatusDto dto, string user, CancellationToken ct);
    Task UpdateCommentAsync(long orderId, UpdateOrderCommentDto dto, string user, CancellationToken ct);
    Task<IReadOnlyList<OrderStatusHistoryDto>> GetHistoryAsync(long orderId, CancellationToken ct);
    Task DeleteAsync(long orderId, string user, CancellationToken ct);
}

public sealed class OrderService(IUnitOfWork uow, IMapper mapper, ILogger<OrderService> logger) : IOrderService
{
    public async Task<OrderDto> GetAsync(long id, CancellationToken ct)
    {
        var order = await uow.Orders.GetWithItemsAsync(id, ct) ?? throw new NotFoundException(nameof(Order), id);
        return mapper.Map<OrderDto>(order);
    }

    public async Task<IReadOnlyList<OrderDto>> GetByCustomerAsync(long customerId, CancellationToken ct)
    {
        _ = await uow.Customers.GetByIdAsync(customerId, ct) ?? throw new NotFoundException(nameof(Customer), customerId);
        var orders = await uow.Orders.GetByCustomerAsync(customerId, ct);
        return mapper.Map<IReadOnlyList<OrderDto>>(orders);
    }

    /// <summary>
    /// Транзакційна бізнес-операція через кілька репозиторіїв: Orders + OrderItems + OrderStatusHistory
    /// в одній транзакції UoW. Ціна й назва позиції — знімок з локального довідника на момент покупки.
    /// Будь-яка помилка → Rollback.
    /// </summary>
    public async Task<OrderDto> CreateAsync(CreateOrderDto dto, string user, CancellationToken ct)
    {
        // Бізнес-правила, що потребують стану БД
        var duplicates = dto.Items.GroupBy(i => i.ProductId).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        if (duplicates.Length > 0)
            throw new DomainValidationException(nameof(dto.Items), $"Duplicate products in order: {string.Join(", ", duplicates)}");

        _ = await uow.Customers.GetByIdAsync(dto.CustomerId, ct) ?? throw new NotFoundException(nameof(Customer), dto.CustomerId);

        var products = await uow.Products.GetManyAsync(dto.Items.Select(i => i.ProductId), ct);
        foreach (var item in dto.Items)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
                throw new NotFoundException("Product", item.ProductId);
            if (!product.IsAvailable)
                throw new BusinessConflictException($"Product {product.ProductId} '{product.Name}' is not available");
        }

        await uow.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var order = new Order
            {
                OrderNumber = GenerateOrderNumber(),
                CustomerId = dto.CustomerId,
                Status = OrderStatus.New,
                Comment = dto.Comment,
                CreatedBy = user
            };
            order.Id = await uow.Orders.AddAsync(order, ct);

            foreach (var item in dto.Items)
            {
                var product = products[item.ProductId];
                await uow.Orders.AddItemAsync(new OrderItem
                {
                    OrderId = order.Id,
                    ProductId = product.ProductId,
                    ProductName = product.Name,   // знімок
                    UnitPrice = product.Price,    // знімок
                    Quantity = item.Quantity
                }, ct);
            }

            await uow.Orders.RecalculateTotalAsync(order.Id, ct);
            await uow.StatusHistory.AddAsync(new OrderStatusHistory
            {
                OrderId = order.Id, FromStatus = null, ToStatus = OrderStatus.New, ChangedBy = user, Reason = "Order created"
            }, ct);

            await uow.CommitAsync(ct);
            logger.LogInformation("Order {OrderId} ({OrderNumber}) created for customer {CustomerId} with {ItemsCount} items",
                order.Id, order.OrderNumber, order.CustomerId, dto.Items.Count);

            return await GetAsync(order.Id, ct);
        }
        catch
        {
            await uow.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<OrderDto> AddItemAsync(long orderId, AddOrderItemDto dto, CancellationToken ct)
    {
        await uow.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        try
        {
            var order = await uow.Orders.GetWithItemsAsync(orderId, ct) ?? throw new NotFoundException(nameof(Order), orderId);
            if (order.Status != OrderStatus.New)
                throw new BusinessConflictException($"Items can be added only to orders in status {OrderStatus.New}");
            if (order.Items.Any(i => i.ProductId == dto.ProductId))
                throw new BusinessConflictException($"Product {dto.ProductId} is already in the order");

            var product = await uow.Products.GetAsync(dto.ProductId, ct) ?? throw new NotFoundException("Product", dto.ProductId);
            if (!product.IsAvailable) throw new BusinessConflictException($"Product {product.ProductId} is not available");

            await uow.Orders.AddItemAsync(new OrderItem
            {
                OrderId = orderId, ProductId = product.ProductId, ProductName = product.Name,
                UnitPrice = product.Price, Quantity = dto.Quantity
            }, ct);
            await uow.Orders.RecalculateTotalAsync(orderId, ct);
            await uow.CommitAsync(ct);
        }
        catch
        {
            await uow.RollbackAsync(CancellationToken.None);
            throw;
        }
        return await GetAsync(orderId, ct);
    }

    public async Task<OrderDto> ChangeStatusAsync(long orderId, ChangeOrderStatusDto dto, string user, CancellationToken ct)
    {
        var order = await uow.Orders.GetByIdAsync(orderId, ct) ?? throw new NotFoundException(nameof(Order), orderId);
        if (!OrderStatus.CanTransition(order.Status, dto.Status))
            throw new BusinessConflictException($"Invalid status transition {order.Status} -> {dto.Status}");

        byte[]? rowVersion = null;
        if (!string.IsNullOrWhiteSpace(dto.RowVersion))
        {
            try { rowVersion = Convert.FromBase64String(dto.RowVersion); }
            catch (FormatException) { throw new DomainValidationException(nameof(dto.RowVersion), "RowVersion must be a base64 string"); }
        }

        // Процедура сама працює в явній транзакції та повторно перевіряє перехід під блокуванням
        await uow.Orders.ChangeStatusAsync(orderId, dto.Status, user, dto.Reason, rowVersion, ct);
        logger.LogInformation("Order {OrderId} status changed {FromStatus} -> {ToStatus} by {User}",
            orderId, order.Status, dto.Status, user);
        return await GetAsync(orderId, ct);
    }

    public async Task UpdateCommentAsync(long orderId, UpdateOrderCommentDto dto, string user, CancellationToken ct)
    {
        var order = await uow.Orders.GetByIdAsync(orderId, ct) ?? throw new NotFoundException(nameof(Order), orderId);
        order.Comment = dto.Comment;
        order.UpdatedBy = user;
        order.RowVersion = Convert.FromBase64String(dto.RowVersion);
        if (!await uow.Orders.UpdateAsync(order, ct))
            throw new BusinessConflictException("Order was modified by another user (concurrency conflict)");
    }

    public async Task<IReadOnlyList<OrderStatusHistoryDto>> GetHistoryAsync(long orderId, CancellationToken ct)
    {
        _ = await uow.Orders.GetByIdAsync(orderId, ct) ?? throw new NotFoundException(nameof(Order), orderId);
        var history = await uow.StatusHistory.GetByOrderAsync(orderId, ct);
        return mapper.Map<IReadOnlyList<OrderStatusHistoryDto>>(history);
    }

    public async Task DeleteAsync(long orderId, string user, CancellationToken ct)
    {
        _ = await uow.Orders.GetByIdAsync(orderId, ct) ?? throw new NotFoundException(nameof(Order), orderId);
        await uow.Orders.SoftDeleteAsync(orderId, user, ct);
        logger.LogInformation("Order {OrderId} soft-deleted by {User}", orderId, user);
    }

    private static string GenerateOrderNumber() =>
        $"ORD-{DateTime.UtcNow:yyMMdd}-{Random.Shared.Next(0, 99999):D5}";
}
