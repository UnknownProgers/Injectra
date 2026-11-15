using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using GroveGames.DependencyInjection;
using GroveContainer = GroveGames.DependencyInjection.IContainer;

namespace Injectra.Benchmarks.Comparison;

[MemoryDiagnoser]
public class FirstResolveComparisonBenchmarks
{
	private Container _injectra = null!;
	private ServiceProvider _microsoft = null!;
	private GroveContainer _grove = null!;

	// Fresh containers for every sample. Build and disposal are excluded from the measured first graph resolve.
	// IterationSetup makes BenchmarkDotNet use one invocation per iteration; these short timings are noisy.
	[IterationSetup]
	public void Setup()
	{
		_injectra = ComparisonContainers.BuildInjectra();
		_microsoft = ComparisonContainers.BuildMicrosoft();
		_grove = ComparisonContainers.BuildGrove();
	}

	[Benchmark(Baseline = true)]
	public Player Injectra() => _injectra.Resolve<Player>();
	[Benchmark]
	public Player Microsoft() => _microsoft.GetRequiredService<Player>();
	[Benchmark]
	public Player Grove() => _grove.Resolve<Player>();

	[IterationCleanup]
	public void Cleanup()
	{
		_grove.Dispose();
		_microsoft.Dispose();
		_injectra.Dispose();
	}
}
