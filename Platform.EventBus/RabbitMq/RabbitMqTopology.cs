using RabbitMQ.Client;

namespace Platform.EventBus.RabbitMq;

/// <summary>
/// Декларативна топологія, що створюється при старті (операції ідемпотентні):
/// durable topic exchange → окрема durable черга сервісу-споживача з x-dead-letter-exchange →
/// DLX (direct) → черга DLQ "&lt;queue&gt;.dlq", прив'язана до DLX за ім'ям основної черги.
/// </summary>
public static class RabbitMqTopology
{
    public static async Task DeclareExchangesAsync(IChannel channel, EventBusOptions options, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(options.ExchangeName, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(options.DeadLetterExchangeName, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: ct);
    }

    public static async Task DeclareConsumerQueueAsync(
        IChannel channel, EventBusOptions options, string queueName, IEnumerable<string> routingKeys, CancellationToken ct)
    {
        await DeclareExchangesAsync(channel, options, ct);

        var dlqName = $"{queueName}.dlq";
        await channel.QueueDeclareAsync(dlqName, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(dlqName, options.DeadLetterExchangeName, routingKey: queueName, cancellationToken: ct);

        await channel.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = options.DeadLetterExchangeName,
                ["x-dead-letter-routing-key"] = queueName
            },
            cancellationToken: ct);

        foreach (var routingKey in routingKeys)
            await channel.QueueBindAsync(queueName, options.ExchangeName, routingKey, cancellationToken: ct);
    }
}
