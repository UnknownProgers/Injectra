using BenchmarkDotNet.Attributes;
using GroveGames.DependencyInjection;
using Injectra.Benchmarks.Comparison;
using Microsoft.Extensions.DependencyInjection;
using GroveContainer = GroveGames.DependencyInjection.IContainer;

namespace Injectra.Benchmarks.Experiments;

// Handwritten models of potential generated output, not a production graph compiler.
// The fixture fixes Clock as singleton and Movement/Player as constructor-only transients.
[MemoryDiagnoser]
public class GraphInliningBenchmarks
{
	private Container _current = null!;
	private Container _boundaries = null!;
	private Container _inline = null!;
	private Container _shared = null!;
	private Container _prebound = null!;
	private ServiceProvider _microsoft = null!;
	private GroveContainer _grove = null!;
	private IClock _clock = null!;

	[GlobalSetup]
	public void Setup()
	{
		_current = ComparisonContainers.BuildInjectra();
		_boundaries = CreateBoundaryFactory();
		_inline = CreateInlineFactory(shareSingleton: false);
		_shared = CreateInlineFactory(shareSingleton: true);
		_prebound = CreatePreboundFactory();
		_clock = _prebound.Resolve<IClock>();
		_microsoft = ComparisonContainers.BuildMicrosoft();
		_grove = ComparisonContainers.BuildGrove();
		CurrentContainer();
		FactoryWithBoundaries();
		InlineMovement();
		InlineWithSharedSingleton();
		InlineWithPreboundSingleton();
		DirectConstructionLowerBound();
		Microsoft();
		Grove();
	}

	[Benchmark(Baseline = true)]
	public Player CurrentContainer() => _current.Resolve<Player>();

	// Preserves dependency bindings for this fixture, including Movement overrides and factories.
	[Benchmark]
	public Player FactoryWithBoundaries() => _boundaries.Resolve<Player>();

	// Preserves IClock lifetime semantics, but assumes Movement remains a constructor-only transient.
	[Benchmark]
	public Player InlineMovement() => _inline.Resolve<Player>();

	// Additionally assumes both clock edges refer to the same singleton binding.
	[Benchmark]
	public Player InlineWithSharedSingleton() => _shared.Resolve<Player>();

	// Bound to this container's clock; invalid for an inherited plan without rebinding and owner checks.
	[Benchmark]
	public Player InlineWithPreboundSingleton() => _prebound.Resolve<Player>();

	// No lookup, cycle detection or lifetime/disposal protection: a lower bound, not an equivalent DI operation.
	[Benchmark]
	public Player DirectConstructionLowerBound() => new(_clock, new Movement(_clock));

	[Benchmark]
	public Player Microsoft() => _microsoft.GetRequiredService<Player>();

	[Benchmark]
	public Player Grove() => _grove.Resolve<Player>();

	public static Container CreateBoundaryFactory()
	{
		var builder = CreateBuilder();
		builder.Register(resolver => new Player(resolver.Resolve<IClock>(), resolver.Resolve<Movement>()), Lifetime.Transient);
		return builder.Build();
	}

	public static Container CreateInlineFactory(bool shareSingleton)
	{
		var builder = CreateBuilder();
		if (shareSingleton)
			builder.Register(resolver =>
			{
				var clock = resolver.Resolve<IClock>();
				return new Player(clock, new Movement(clock));
			}, Lifetime.Transient);
		else
			builder.Register(resolver => new Player(
				resolver.Resolve<IClock>(),
				new Movement(resolver.Resolve<IClock>())), Lifetime.Transient);
		return builder.Build();
	}

	public static Container CreatePreboundFactory()
	{
		IClock clock = null!;
		var builder = CreateBuilder();
		builder.Register(_ => new Player(clock, new Movement(clock)), Lifetime.Transient);
		var container = builder.Build();
		clock = container.Resolve<IClock>();
		return container;
	}

	private static ContainerBuilder CreateBuilder()
	{
		var builder = new ContainerBuilder();
		builder.Register<Clock>(Lifetime.Singleton).As<IClock>();
		builder.Register<Enemy>(Lifetime.Transient);
		builder.Register<Movement>(Lifetime.Transient);
		return builder;
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_grove.Dispose();
		_microsoft.Dispose();
		_prebound.Dispose();
		_shared.Dispose();
		_inline.Dispose();
		_boundaries.Dispose();
		_current.Dispose();
	}
}
