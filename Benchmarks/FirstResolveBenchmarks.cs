using BenchmarkDotNet.Attributes;

namespace Injectra.Benchmarks;

[MemoryDiagnoser]
public class FirstResolveBenchmarks
{
	[Params(32, 256, 1024)]
	public int Registrations { get; set; }

	[Params(1, 4, 8)]
	public int Depth { get; set; }

	private HierarchyFixture _fixture = null!;
	private Container _child = null!;

	[GlobalSetup]
	public void Setup() => _fixture = new HierarchyFixture(Registrations, Depth);

	// BenchmarkDotNet runs one invocation per iteration when iteration setup is present.
	// Child construction and disposal are outside the measured first resolve.
	[IterationSetup]
	public void CreateChild() => _child = _fixture.CreateChild();

	[Benchmark]
	public object ConstructorGraph() => _child.Resolve<LayoutMeasurements.Consumer>();

	[IterationCleanup]
	public void DisposeChild() => _child.Dispose();

	[GlobalCleanup]
	public void Cleanup() => _fixture.Dispose();
}
