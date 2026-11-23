using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.ServiceDefaults.Authorization;

namespace Payments.Api;

public sealed record PaymentDto(Guid Id, long OrderId, decimal Amount, string Status, string CreatedBy, DateTime CreatedAt);
public sealed record CreatePaymentRequest([Range(1, long.MaxValue)] long OrderId, [Range(0.01, 1_000_000)] decimal Amount);

/// <summary>Заглушка сховища (демо-сервіс без БД).</summary>
public sealed class PaymentStore
{
    public ConcurrentDictionary<Guid, PaymentDto> Items { get; } = new();
}

/// <summary>Кожен ендпоінт захищено [RequirePermission] — перевіряється claim authorization.permissions з токена.</summary>
[ApiController]
[Authorize]
[Route("api/payments")]
public sealed class PaymentsController(PaymentStore store) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Payments.Read)]
    public ActionResult<IEnumerable<PaymentDto>> GetAll() => Ok(store.Items.Values.OrderByDescending(p => p.CreatedAt));

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.Payments.Read)]
    public ActionResult<PaymentDto> GetById(Guid id) => store.Items.TryGetValue(id, out var p) ? Ok(p) : NotFound();

    [HttpPost]
    [RequirePermission(Permissions.Payments.Create)]
    public ActionResult<PaymentDto> Create(CreatePaymentRequest request)
    {
        var user = User.FindFirstValue("preferred_username") ?? User.FindFirstValue("username") ?? User.FindFirstValue("sub") ?? "unknown";
        var payment = new PaymentDto(Guid.NewGuid(), request.OrderId, request.Amount, "Succeeded", user, DateTime.UtcNow);
        store.Items[payment.Id] = payment;
        return CreatedAtAction(nameof(GetById), new { id = payment.Id }, payment);
    }

    [HttpPost("{id:guid}/refund")]
    [RequirePermission(Permissions.Payments.Refund)]
    public ActionResult<PaymentDto> Refund(Guid id)
    {
        if (!store.Items.TryGetValue(id, out var p)) return NotFound();
        var refunded = p with { Status = "Refunded" };
        store.Items[id] = refunded;
        return Ok(refunded);
    }
}
