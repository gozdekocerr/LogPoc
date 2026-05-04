using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace API.HealthChecks;

public sealed class LogFileHealthCheck : IHealthCheck
{
    private const string LogDirectory = "logs";

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(LogDirectory))
                Directory.CreateDirectory(LogDirectory);

            var testFile = Path.Combine(LogDirectory, ".healthcheck");
            File.WriteAllText(testFile, DateTimeOffset.UtcNow.ToString("O"));
            File.Delete(testFile);

            return Task.FromResult(HealthCheckResult.Healthy("Log klasörü yazılabilir."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Log klasörüne yazılamıyor.",
                exception: ex));
        }
    }
}
