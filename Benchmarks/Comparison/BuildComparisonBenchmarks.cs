using BenchmarkDotNet.Attributes;

namespace Injectra.Benchmarks.Comparison;

[MemoryDiagnoser]
public class BuildComparisonBenchmarks
{
	// Includes registration, build-time validation and disposal. No service instances are resolved.
	[Benchmark(Baseline = true)]
	public void Injectra()
	{
		using var container = ComparisonContainers.BuildInjectra();
	}

	[Benchmark]
	public void Microsoft()
	{
		using var container = ComparisonContainers.BuildMicrosoft();
	}

	[Benchmark]
	public void Grove()
	{
		using var container = ComparisonContainers.BuildGrove();
	}
}
