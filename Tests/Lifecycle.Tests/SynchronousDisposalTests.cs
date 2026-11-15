using Xunit;
using static Injectra.Lifecycle.Tests.RootLifecycleIntegrationTests;

namespace Injectra.Lifecycle.Tests;

public sealed class SynchronousDisposalTests
{
	[Fact]
	public void ClosingParentStopsRemainingParticipantsAndGrandchildrenWithoutStoppingSiblings()
	{
		using var root = new ContainerBuilder().UseLifecycle().Build();
		var first = new Probe();
		var second = new Probe();
		var nested = new Probe();
		var sibling = new Probe();
		using var level = root.CreateChild(b =>
		{
			Register(b, first);
			Register(b, second);
		});
		using var grandchild = level.CreateChild(b => Register(b, nested));
		using var other = root.CreateChild(b => Register(b, sibling));
		first.OnTick = second.OnTick = nested.OnTick = () =>
		{
			level.Dispose();
			Assert.Equal(3, first.Disposals + second.Disposals + nested.Disposals);
			Assert.Throws<ObjectDisposedException>(() => grandchild.Resolve<IFrameTickable>());
		};
		var lifecycle = root.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		lifecycle.Tick(0.1);
		Assert.Equal(1, first.Ticks + second.Ticks + nested.Ticks);
		Assert.Equal(3, first.Disposals + second.Disposals + nested.Disposals);
		Assert.Equal(1, sibling.Ticks);
		lifecycle.Tick(0.1);
		Assert.Equal(2, sibling.Ticks);
		Assert.Equal(1, first.Ticks + second.Ticks + nested.Ticks);
	}

	[Fact]
	public void ClosingDuringInitializationSkipsRemainingInitializersAndStillCleansUp()
	{
		using var root = new ContainerBuilder().UseLifecycle().Build();
		var first = new Probe();
		var second = new Probe();
		using var child = root.CreateChild(b =>
		{
			Register(b, first);
			Register(b, second);
		});
		first.OnInitialize = second.OnInitialize = () => child.Dispose();
		var lifecycle = root.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		Assert.Equal(1, first.Initializations + second.Initializations);
		Assert.Equal(2, first.Disposals + second.Disposals);
		lifecycle.Tick(0.1);
		Assert.Equal(0, first.Ticks + second.Ticks);
	}

	[Fact]
	public void ClosingDuringCustomPhaseAlsoDisconnectsOtherPhases()
	{
		var builder = new ContainerBuilder().UseLifecycle(l =>
			l.RegisterPhase<IPhysics, double>(static (p, dt) => p.Physics(dt)));
		using var root = builder.Build();
		var probe = new Probe();
		using var child = root.CreateChild(b => Register(b, probe));
		probe.OnPhysics = () => child.Dispose();
		var lifecycle = root.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		lifecycle.Dispatch<IPhysics, double>(0.02);
		Assert.Equal(1, probe.Disposals);
		lifecycle.Tick(0.1);
		lifecycle.Dispatch<IPhysics, double>(0.02);
		Assert.Equal(0, probe.Ticks);
		Assert.Equal(1, probe.PhysicsCalls);
	}

	[Fact]
	public void CleanupFailurePropagatesFromDisposeAndStopsTheCurrentCallback()
	{
		using var root = new ContainerBuilder().UseLifecycle().Build();
		var cleanupError = new ApplicationException("cleanup");
		var first = new Probe { OnDispose = () => throw cleanupError };
		var second = new Probe();
		using var child = root.CreateChild(b => Register(b, first));
		using var sibling = root.CreateChild(b => Register(b, second));
		first.OnTick = () =>
		{
			var error = Assert.Throws<AggregateException>(() => child.Dispose());
			Assert.Contains(cleanupError, error.Flatten().InnerExceptions);
			throw error;
		};
		var lifecycle = root.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		var error = Assert.Throws<AggregateException>(() => lifecycle.Tick(0.1));
		Assert.Contains(cleanupError, error.Flatten().InnerExceptions);
		Assert.Equal(1, first.Disposals);
		Assert.Equal(0, second.Disposals);
		lifecycle.Tick(0.1);
		Assert.Equal(1, second.Ticks);
	}

	[Fact]
	public void NewlyCreatedChildClosedInTheSameTickIsNeverInitialized()
	{
		var creator = new Probe();
		var spawned = new Probe();
		var builder = new ContainerBuilder().UseLifecycle();
		Register(builder, creator);
		using var root = builder.Build();
		creator.OnTick = () =>
		{
			creator.OnTick = null;
			var child = root.CreateChild(b => Register(b, spawned));
			child.Resolve<IFrameTickable>();
			child.Dispose();
			Assert.Equal(1, spawned.Disposals);
		};
		var lifecycle = root.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		lifecycle.Tick(0.1);
		Assert.Equal(0, spawned.Initializations);
		Assert.Equal(0, spawned.Ticks);
		Assert.Equal(1, spawned.Disposals);
	}

	[Fact]
	public void ChildCanCloseItselfDuringDeferredInitialization()
	{
		var creator = new Probe();
		var spawned = new Probe();
		var builder = new ContainerBuilder().UseLifecycle();
		Register(builder, creator);
		using var root = builder.Build();
		creator.OnTick = () =>
		{
			creator.OnTick = null;
			var child = root.CreateChild(b => Register(b, spawned));
			spawned.OnInitialize = () => child.Dispose();
		};
		var lifecycle = root.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		lifecycle.Tick(0.1);
		Assert.Equal(1, spawned.Initializations);
		Assert.Equal(1, spawned.Disposals);
		lifecycle.Tick(0.1);
		Assert.Equal(0, spawned.Ticks);
	}

	[Fact]
	public void InitializationFailureStillFlushesRequestedClosuresAndDisposesPendingChildren()
	{
		var creator = new Probe();
		var pending = new Probe();
		var expected = new ApplicationException("initialize");
		var builder = new ContainerBuilder().UseLifecycle();
		Register(builder, creator);
		using var root = builder.Build();
		var existing = new Probe();
		using var child = root.CreateChild(b => Register(b, existing));
		Container? created = null;
		creator.OnInitialize = () =>
		{
			child.Dispose();
			created = root.CreateChild(b => Register(b, pending));
			created.Resolve<IFrameTickable>();
			throw expected;
		};
		var lifecycle = root.Resolve<InjectraLifecycle>();
		Assert.Same(expected, Assert.Throws<ApplicationException>(() => lifecycle.Initialize()));
		Assert.Equal(1, existing.Disposals);
		Assert.Equal(1, pending.Disposals);
		Assert.Throws<ObjectDisposedException>(() => created!.Resolve<IFrameTickable>());
		Assert.Throws<InvalidOperationException>(() => lifecycle.Tick(0.1));
	}

	[Fact]
	public void OverlappingClosureRequestsDisposeEachOwnedServiceOnceInHierarchyOrder()
	{
		var order = new List<string>();
		using var root = new ContainerBuilder().UseLifecycle().Build();
		var parentProbe = new Probe { OnDispose = () => order.Add("parent") };
		var childProbe = new Probe { OnDispose = () => order.Add("child") };
		using var level = root.CreateChild(b => Register(b, parentProbe));
		using var nested = level.CreateChild(b => Register(b, childProbe));
		parentProbe.OnTick = childProbe.OnTick = () =>
		{
			nested.Dispose();
			level.Dispose();
			level.Dispose();
		};
		var lifecycle = root.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		lifecycle.Tick(0.1);
		Assert.Equal(new[] { "child", "parent" }, order);
		Assert.Equal(1, parentProbe.Disposals);
		Assert.Equal(1, childProbe.Disposals);
	}

	[Fact]
	public void ClosureDetachesExternalInstancesWithoutDisposingThem()
	{
		using var root = new ContainerBuilder().UseLifecycle().Build();
		var probe = new Probe();
		using var child = root.CreateChild(b => b.RegisterInstance(probe).AsImplementedInterfaces());
		probe.OnTick = () => child.Dispose();
		var lifecycle = root.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		lifecycle.Tick(0.1);
		lifecycle.Tick(0.1);
		Assert.Equal(1, probe.Ticks);
		Assert.Equal(0, probe.Disposals);
	}

	[Fact]
	public void EveryDispatcherInTheSubtreeStopsBeforeUserCleanupCanDispatchAgain()
	{
		using var root = new ContainerBuilder().UseLifecycle().Build();
		var parentProbe = new Probe();
		var childProbe = new Probe();
		var siblingProbe = new Probe();
		using var level = root.CreateChild(b => Register(b, parentProbe));
		using var nested = level.CreateChild(b => Register(b, childProbe));
		using var sibling = root.CreateChild(b => Register(b, siblingProbe));
		var lifecycle = root.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		childProbe.OnDispose = () => lifecycle.Tick(0.1);
		level.Dispose();
		Assert.Equal(0, parentProbe.Ticks);
		Assert.Equal(0, childProbe.Ticks);
		Assert.Equal(1, siblingProbe.Ticks);
	}

	[Fact]
	public void StoppedDispatcherDoesNotPreventReusingAnExternalParticipant()
	{
		using var root = new ContainerBuilder().UseLifecycle().Build();
		var probe = new Probe();
		var first = root.CreateChild(b => b.RegisterInstance(probe).As<IFrameTickable>());
		var lifecycle = root.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		first.Dispose();
		using var second = root.CreateChild(b => b.RegisterInstance(probe).As<IFrameTickable>());
		lifecycle.Tick(0.1);
		Assert.Equal(1, probe.Ticks);
		Assert.Equal(0, probe.Disposals);
	}

	private static void Register(ContainerBuilder builder, Probe probe) =>
		builder.Register<Probe>(_ => probe, Lifetime.Singleton).AsImplementedInterfaces();

	private sealed class Probe : IInitializable, IFrameTickable, IPhysics, IDisposable
	{
		internal int Initializations, Ticks, PhysicsCalls, Disposals;
		internal Action? OnInitialize, OnTick, OnPhysics, OnDispose;

		public void Initialize()
		{
			Initializations++;
			OnInitialize?.Invoke();
		}

		public void FrameTick(double deltaTime)
		{
			Ticks++;
			OnTick?.Invoke();
		}

		public void Physics(double deltaTime)
		{
			PhysicsCalls++;
			OnPhysics?.Invoke();
		}

		public void Dispose()
		{
			Disposals++;
			OnDispose?.Invoke();
		}
	}
}
