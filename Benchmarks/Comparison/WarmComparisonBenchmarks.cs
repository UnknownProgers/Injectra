using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using GroveGames.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using GroveContainer = GroveGames.DependencyInjection.IContainer;

namespace Injectra.Benchmarks.Comparison;

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class WarmComparisonBenchmarks
{
	private Container _injectra = null!;
	private ServiceProvider _microsoft = null!;
	private GroveContainer _grove = null!;

	[GlobalSetup]
	public void Setup()
	{
		_injectra = ComparisonContainers.BuildInjectra();
		_microsoft = ComparisonContainers.BuildMicrosoft();
		_grove = ComparisonContainers.BuildGrove();
		// Prepare every measured path. BenchmarkDotNet additionally warms up the methods themselves.
		InjectraSingleton();
		MicrosoftSingleton();
		GroveSingleton();
		InjectraTransient();
		MicrosoftTransient();
		GroveTransient();
		InjectraGraph();
		MicrosoftGraph();
		GroveGraph();
	}

	[Benchmark(Baseline = true), BenchmarkCategory("Singleton")]
	public IClock InjectraSingleton() => _injectra.Resolve<IClock>();

	[Benchmark, BenchmarkCategory("Singleton")]
	public IClock MicrosoftSingleton() => _microsoft.GetRequiredService<IClock>();

	[Benchmark, BenchmarkCategory("Singleton")]
	public IClock GroveSingleton() => _grove.Resolve<IClock>();

	[Benchmark(Baseline = true), BenchmarkCategory("Transient")]
	public Enemy InjectraTransient() => _injectra.Resolve<Enemy>();

	[Benchmark, BenchmarkCategory("Transient")]
	public Enemy MicrosoftTransient() => _microsoft.GetRequiredService<Enemy>();

	[Benchmark, BenchmarkCategory("Transient")]
	public Enemy GroveTransient() => _grove.Resolve<Enemy>();

	[Benchmark(Baseline = true), BenchmarkCategory("Graph")]
	public Player InjectraGraph() => _injectra.Resolve<Player>();

	[Benchmark, BenchmarkCategory("Graph")]
	public Player MicrosoftGraph() => _microsoft.GetRequiredService<Player>();

	[Benchmark, BenchmarkCategory("Graph")]
	public Player GroveGraph() => _grove.Resolve<Player>();

	[GlobalCleanup]
	public void Cleanup()
	{
		_grove.Dispose();
		_microsoft.Dispose();
		_injectra.Dispose();
	}
}
