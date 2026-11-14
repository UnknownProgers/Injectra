using BenchmarkDotNet.Attributes;

namespace Injectra.Benchmarks;

[MemoryDiagnoser]
public class ChildCreationBenchmarks
{
	[Params(32, 256, 1024)]
	public int Registrations { get; set; }

	[Params(1, 4, 8)]
	public int Depth { get; set; }

	private HierarchyFixture _fixture = null!;

	[GlobalSetup]
	public void Setup() => _fixture = new HierarchyFixture(Registrations, Depth);

	[Benchmark]
	public void CreateAndDispose()
	{
		using var child = _fixture.Parent.CreateChild();
	}

	[GlobalCleanup]
	public void Cleanup() => _fixture.Dispose();
}
