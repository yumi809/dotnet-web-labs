using Grpc.Core;
using Grpc.Health.V1;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Platform.Aggregator.HealthChecks;

/// <summary>
/// Перевірка gRPC-залежності Aggregator через стандартний Health Checking Protocol (Health.HealthClient.Check)
/// з коротким дедлайном. RpcException → Unhealthy (або failureStatus реєстрації), збій логується.
/// Перевірка ідемпотентна й не має побічних ефектів у сервісі, що перевіряється.
/// </summary>
public sealed class GrpcDependencyHealthCheck(
    GrpcClientFactory clientFactory,
    ILogger<GrpcDependencyHealthCheck> logger,
    string clientName) : IHealthCheck
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(2);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var client = clientFactory.CreateClient<Health.HealthClient>(clientName);
        try
        {
            var reply = await client.CheckAsync(new HealthCheckRequest(), deadline: DateTime.UtcNow.Add(Deadline), cancellationToken: ct);
            return reply.Status == HealthCheckResponse.Types.ServingStatus.Serving
                ? HealthCheckResult.Healthy($"{context.Registration.Name} is serving")
                : new HealthCheckResult(context.Registration.FailureStatus, $"{context.Registration.Name} status: {reply.Status}");
        }
        catch (RpcException ex)
        {
            logger.LogWarning("Health check of {Dependency} failed with {GrpcStatus}: {Detail}", context.Registration.Name, ex.StatusCode, ex.Status.Detail);
            return new HealthCheckResult(context.Registration.FailureStatus, $"{context.Registration.Name} is unavailable ({ex.StatusCode})", ex);
        }
    }
}
