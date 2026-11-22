using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;

namespace Platform.ServiceDefaults.Grpc;

/// <summary>
/// Серверний interceptor: метод, peer, статус і тривалість кожного gRPC-виклику — структурованими властивостями.
/// Підключається в сервісах: AddGrpc(o => o.Interceptors.Add&lt;ServerLoggingInterceptor&gt;()).
/// </summary>
public sealed class ServerLoggingInterceptor(ILogger<ServerLoggingInterceptor> logger) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await continuation(request, context);
            logger.LogInformation("gRPC {GrpcMethod} from {Peer} completed with {GrpcStatus} in {ElapsedMs} ms",
                context.Method, context.Peer, StatusCode.OK, sw.ElapsedMilliseconds);
            return response;
        }
        catch (RpcException ex)
        {
            var level = ex.StatusCode is StatusCode.Internal or StatusCode.Unknown ? LogLevel.Error : LogLevel.Warning;
            logger.Log(level, "gRPC {GrpcMethod} from {Peer} failed with {GrpcStatus} in {ElapsedMs} ms: {Detail}",
                context.Method, context.Peer, ex.StatusCode, sw.ElapsedMilliseconds, ex.Status.Detail);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "gRPC {GrpcMethod} from {Peer} failed with {GrpcStatus} in {ElapsedMs} ms",
                context.Method, context.Peer, StatusCode.Internal, sw.ElapsedMilliseconds);
            throw new RpcException(new Status(StatusCode.Internal, "Internal error"));
        }
    }
}
