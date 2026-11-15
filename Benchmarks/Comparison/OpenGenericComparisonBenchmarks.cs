using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;

namespace Injectra.Benchmarks.Comparison;

// Grove 0.8.1 does not support resolving closed contracts from open generic registrations.
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class OpenGenericComparisonBenchmarks
{
	private Container _injectra = null!;
	private ServiceProvider _microsoft = null!;

	[GlobalSetup]
	public void Setup()
	{
		_injectra = ComparisonContainers.BuildGenericInjectra();
		_microsoft = ComparisonContainers.BuildGenericMicrosoft();
		InjectraSingleton();
		MicrosoftSingleton();
		InjectraGraph();
		MicrosoftGraph();
	}

	[Benchmark(Baseline = true), BenchmarkCategory("Singleton")]
	public IRepository<int> InjectraSingleton() => _injectra.Resolve<IRepository<int>>();
	[Benchmark, BenchmarkCategory("Singleton")]
	public IRepository<int> MicrosoftSingleton() => _microsoft.GetRequiredService<IRepository<int>>();

	[Benchmark(Baseline = true), BenchmarkCategory("Graph")]
	public GenericConsumer<int> InjectraGraph() => _injectra.Resolve<GenericConsumer<int>>();
	[Benchmark, BenchmarkCategory("Graph")]
	public GenericConsumer<int> MicrosoftGraph() => _microsoft.GetRequiredService<GenericConsumer<int>>();

	[GlobalCleanup]
	public void Cleanup()
	{
		_microsoft.Dispose();
		_injectra.Dispose();
	}
}

[MemoryDiagnoser]
public class FirstOpenGenericComparisonBenchmarks
{
	private Container _injectra = null!;
	private ServiceProvider _microsoft = null!;

	[IterationSetup]
	public void Setup()
	{
		_injectra = ComparisonContainers.BuildGenericInjectra();
		_microsoft = ComparisonContainers.BuildGenericMicrosoft();
	}

	[Benchmark(Baseline = true)]
	public GenericConsumer<int> Injectra() => _injectra.Resolve<GenericConsumer<int>>();
	[Benchmark]
	public GenericConsumer<int> Microsoft() => _microsoft.GetRequiredService<GenericConsumer<int>>();

	[IterationCleanup]
	public void Cleanup()
	{
		_microsoft.Dispose();
		_injectra.Dispose();
	}
}
