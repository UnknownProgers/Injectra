using BenchmarkDotNet.Attributes;
using Injectra.Lifecycle;

namespace Injectra.Benchmarks;

[MemoryDiagnoser]
public class LifecycleDispatchBenchmarks
{
	[Params(1, 8, 64)]
	public int Children { get; set; }

	[Params(0, 4)]
	public int ParticipantsPerChild { get; set; }

	private LifecycleFixture _fixture = null!;
	private InjectraLifecycle _lifecycle = null!;

	[GlobalSetup]
	public void Setup()
	{
		_fixture = new LifecycleFixture(true, Children, ParticipantsPerChild);
		_lifecycle = _fixture.Root.Resolve<InjectraLifecycle>();
	}

	[Benchmark]
	public void Tick() => _lifecycle.Tick(1.0 / 60);

	[GlobalCleanup]
	public void Cleanup() => _fixture.Dispose();
}
[MemoryDiagnoser]
public class LifecycleScopeBenchmarks
{
	[Params(0, 8, 64)]
	public int ExistingChildren { get; set; }

	[Params(0, 4)]
	public int ParticipantsPerChild { get; set; }

	private LifecycleFixture _core = null!;
	private LifecycleFixture _lifecycle = null!;

	[GlobalSetup]
	public void Setup()
	{
		_core = new LifecycleFixture(false, ExistingChildren, ParticipantsPerChild);
		_lifecycle = new LifecycleFixture(true, ExistingChildren, ParticipantsPerChild);
	}

	[Benchmark(Baseline = true)]
	public void CoreCreateAndDispose()
	{
		using var child = _core.CreateChild();
	}

	[Benchmark]
	public void LifecycleCreateAndDispose()
	{
		using var child = _lifecycle.CreateChild();
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_core.Dispose();
		_lifecycle.Dispose();
	}
}
public sealed class LifecycleFixture : IDisposable
{
	private readonly Action<ContainerBuilder> _configure;
	public Container Root { get; }

	public LifecycleFixture(bool lifecycle, int children, int participants)
	{
		_configure = builder =>
		{
			for (var i = 0; i < participants; i++)
				builder.Register<LifecycleParticipant>(static _ => new LifecycleParticipant(), Lifetime.Singleton)
					.AsImplementedInterfaces().NonLazy();
		};
		var builder = new ContainerBuilder();
		if (lifecycle) builder.UseLifecycle();
		Root = builder.Build();
		if (lifecycle) Root.Resolve<InjectraLifecycle>().Initialize();
		for (var i = 0; i < children; i++) CreateChild();
	}

	public Container CreateChild() => Root.CreateChild(_configure);
	public void Dispose() => Root.Dispose();
}
public sealed class LifecycleParticipant : IInitializable, IFrameTickable, IDisposable
{
	public long Frames { get; private set; }
	public int Initializations { get; private set; }
	public int Disposals { get; private set; }
	public void Initialize() => Initializations++;
	public void FrameTick(double deltaTime) => Frames++;
	public void Dispose() => Disposals++;
}
