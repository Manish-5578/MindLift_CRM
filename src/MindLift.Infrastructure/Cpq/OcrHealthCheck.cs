using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MindLift.Infrastructure.Cpq;

public sealed class OcrHealthCheck(IOcrEngine ocrEngine) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (ocrEngine.IsAvailable())
        {
            return Task.FromResult(HealthCheckResult.Healthy("Tesseract OCR engine is available."));
        }

        return Task.FromResult(HealthCheckResult.Degraded("Tesseract OCR CLI not found; digital text and simulated extraction active."));
    }
}
