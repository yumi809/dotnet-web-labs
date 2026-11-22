using Platform.EventBus.Abstractions;

namespace Platform.EventBus.Events;

// Контракти подій платформи (власник — сервіс у префіксі routing key).

/// <summary>Каталог: товар змінено (назва/ціна/доступність). Споживачі: Orders (довідник), Reviews (productName).</summary>
public sealed record ProductUpdatedIntegrationEvent(
    long ProductId,
    string Name,
    decimal Price,
    bool IsActive) : IntegrationEvent
{
    public override string RoutingKey => "catalog.product.updated";
}

/// <summary>Каталог: змінено ціну товару (окрема подія для аналітики/сповіщень).</summary>
public sealed record ProductPriceChangedIntegrationEvent(
    long ProductId,
    string Name,
    decimal OldPrice,
    decimal NewPrice) : IntegrationEvent
{
    public override string RoutingKey => "catalog.product.price-changed";
}

public sealed record OrderLineSnapshot(long ProductId, int Quantity);

/// <summary>Замовлення: створено замовлення. Споживач: Catalog (резервування запасу).</summary>
public sealed record OrderCreatedIntegrationEvent(
    long OrderId,
    string OrderNumber,
    long CustomerId,
    IReadOnlyList<OrderLineSnapshot> Lines) : IntegrationEvent
{
    public override string RoutingKey => "orders.order.created";
}
