namespace PatchGuard.Services.Performance;

public interface ISyntheticBenchmarkRunner
{
    Task<double> RunAsync(CancellationToken cancellationToken = default);
}
