using BenchmarkDotNet.Attributes;

namespace Injectra.Benchmarks;

[MemoryDiagnoser]
public class WarmResolveBenchmarks
{
	[Params(32, 256, 1024)]
	public int Registrations { get; set; }

	[Params(1, 4, 8)]
	public int Depth { get; set; }

	private HierarchyFixture _fixture = null!;

	[GlobalSetup]
	public void Setup()
	{
		_fixture = new HierarchyFixture(Registrations, Depth);
		_fixture.Leaf.Resolve(LayoutMeasurements.Services[0]);
		_fixture.Leaf.Resolve<LayoutMeasurements.Consumer>();
	}

	[Benchmark]
	public object Singleton() => _fixture.Leaf.Resolve(LayoutMeasurements.Services[0]);

	[Benchmark]
	public object ConstructorGraph() => _fixture.Leaf.Resolve<LayoutMeasurements.Consumer>();

	[GlobalCleanup]
	public void Cleanup() => _fixture.Dispose();
}
