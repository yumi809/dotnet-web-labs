namespace Auth.Api.Services;

public interface IAuthEmailSender
{
    Task SendAsync(string email, string subject, string body, CancellationToken ct);
}

/// <summary>Навчальна заглушка: лист «відправляється» в лог (у production — SMTP / SendGrid).</summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IAuthEmailSender
{
    public Task SendAsync(string email, string subject, string body, CancellationToken ct)
    {
        logger.LogInformation("EMAIL to {Email}: {Subject}\n{Body}", email, subject, body);
        return Task.CompletedTask;
    }
}
