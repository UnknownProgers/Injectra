using BenchmarkDotNet.Attributes;

namespace Injectra.Benchmarks.Experiments;

// Real container lookup, after warming inherited and closed generic caches.
[MemoryDiagnoser]
public class HandleIntegrationBenchmarks
{
	[Params(LookupScenario.Local, LookupScenario.Inherited, LookupScenario.ClosedGeneric)]
	public LookupScenario Scenario { get; set; }
	private Container _root = null!;
	private Container _requesting = null!;
	private Type _contract = null!;

	[GlobalSetup]
	public void Setup()
	{
		var builder = new ContainerBuilder();
		LayoutMeasurements.RegisterFillers(builder, 32);
		if (Scenario == LookupScenario.ClosedGeneric)
		{
			builder.Register(typeof(Service<>), Lifetime.Singleton);
			_contract = typeof(Service<int>);
		}
		else
		{
			builder.Register<Service>(Lifetime.Singleton);
			_contract = typeof(Service);
		}
		_root = builder.Build();
		_requesting = Scenario == LookupScenario.Inherited ? _root.CreateChild() : _root;
		Resolve();
	}

	[Benchmark]
	public object Resolve() => _requesting.Resolve(_contract);

	[GlobalCleanup]
	public void Cleanup() => _root.Dispose();

	public sealed class Service;
	public sealed class Service<T>;
}
