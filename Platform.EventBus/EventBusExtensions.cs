using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Platform.EventBus.Abstractions;
using Platform.EventBus.RabbitMq;

namespace Platform.EventBus;

public static class EventBusExtensions
{
    public const string ConnectionName = "messaging";

    /// <summary>
    /// Підключення до RabbitMQ через Aspire.RabbitMQ.Client: AddRabbitMQClient("messaging") реєструє IConnection
    /// (RabbitMQ.Client 7.x, асинхронний IChannel) з health check і трасуванням; плюс publisher.
    /// </summary>
    public static IHostApplicationBuilder AddEventBus(this IHostApplicationBuilder builder)
    {
        builder.AddRabbitMQClient(ConnectionName);
        builder.Services.AddOptions<EventBusOptions>().BindConfiguration(EventBusOptions.SectionName);
        builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
        return builder;
    }

    /// <summary>Реєструє споживача з власною чергою; кожна подія — окремий обробник.</summary>
    public static ConsumerBuilder AddEventConsumer(this IHostApplicationBuilder builder, string queueName)
    {
        var registration = new ConsumerRegistration { QueueName = queueName };
        builder.Services.AddSingleton(registration);
        builder.Services.AddHostedService<RabbitMqConsumerService>();
        return new ConsumerBuilder(builder.Services, registration);
    }
}

public sealed class ConsumerBuilder(IServiceCollection services, ConsumerRegistration registration)
{
    public ConsumerBuilder Subscribe<TEvent, THandler>(string routingKey)
        where TEvent : IntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        registration.Subscriptions[typeof(TEvent).Name] = (typeof(TEvent), routingKey);
        services.AddScoped<IIntegrationEventHandler<TEvent>, THandler>();
        return this;
    }
}
