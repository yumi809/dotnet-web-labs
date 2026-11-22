using System.Diagnostics;
using System.Text;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace Platform.EventBus.RabbitMq;

/// <summary>
/// Передача контексту трасування через брокер: W3C traceparent/tracestate впроваджуються в заголовки при публікації
/// і витягуються в споживачі, який стартує Activity типу Consumer з тегами messaging.*.
/// ActivitySource "BookHub.EventBus" підключено в ServiceDefaults (AddSource).
/// </summary>
public static class EventBusTelemetry
{
    public const string SourceName = "BookHub.EventBus";
    public static readonly ActivitySource ActivitySource = new(SourceName, "1.0.0");
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    public static void Inject(Activity? activity, IDictionary<string, object?> headers)
    {
        var context = activity?.Context ?? Activity.Current?.Context ?? default;
        Propagator.Inject(new PropagationContext(context, Baggage.Current), headers,
            static (carrier, key, value) => carrier[key] = value);
    }

    public static PropagationContext Extract(IDictionary<string, object?>? headers) =>
        Propagator.Extract(default, headers, static (carrier, key) =>
        {
            if (carrier is null || !carrier.TryGetValue(key, out var value) || value is null) return [];
            return [value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : value.ToString()!];
        });

    public static void SetMessagingTags(Activity? activity, string operation, string destination, string? messageId, string? eventType)
    {
        if (activity is null) return;
        activity.SetTag("messaging.system", "rabbitmq");
        activity.SetTag("messaging.operation", operation);
        activity.SetTag("messaging.destination.name", destination);
        activity.SetTag("messaging.message.id", messageId);
        activity.SetTag("messaging.message.type", eventType);
    }
}
