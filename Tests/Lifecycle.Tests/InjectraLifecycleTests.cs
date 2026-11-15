using System.Runtime.CompilerServices;
using Xunit;

namespace Injectra.Lifecycle.Tests;

public sealed class InjectraLifecycleTests
{
	[Fact]
	public void InitializationIsExplicitAndRunsOnce()
	{
		var participant = new Participant();
		using var setup = new LifecycleSetup([participant]);
		var lifecycle = setup.Lifecycle;
		Assert.Equal(0, participant.Initializations);
		Assert.Throws<InvalidOperationException>(() => lifecycle.Tick(0.1));
		lifecycle.Initialize();
		lifecycle.Tick(0.1);
		lifecycle.Tick(0.2);
		Assert.Equal(1, participant.Initializations);
		Assert.Equal(2, participant.Frames);
		Assert.Equal(0.2, participant.FrameDelta);
		Assert.Throws<InvalidOperationException>(() => lifecycle.Initialize());
	}

	[Fact]
	public void CustomPhaseInvokesOnlyMatchingParticipantsWithItsArgument()
	{
		var participant = new Participant();
		using var setup = new LifecycleSetup([participant, new object()]);
		var lifecycle = setup.Lifecycle;
		lifecycle.Initialize();
		lifecycle.Dispatch<IPhysicsTick, double>(0.02);
		Assert.Equal(1, participant.PhysicsCalls);
		Assert.Equal(0.02, participant.PhysicsDelta);
		Assert.Equal(0, participant.Frames);
		Assert.Throws<InvalidOperationException>(() => lifecycle.Dispatch<IPhysicsTick, float>(0.02f));
		Assert.Throws<InvalidOperationException>(() => lifecycle.Dispatch<IUnknown, double>(0.02));
	}

	[Fact]
	public void EveryParticipantIsInitializedBeforeAnyFrameDispatch()
	{
		var first = new Participant();
		var second = new Participant();
		first.Ticking = second.Ticking = () =>
		{
			Assert.Equal(1, first.Initializations);
			Assert.Equal(1, second.Initializations);
		};
		using var setup = new LifecycleSetup([first, second]);
		setup.Lifecycle.Initialize();
		setup.Lifecycle.Tick(0.1);
		Assert.Equal(1, first.Frames);
		Assert.Equal(1, second.Frames);
	}

	[Fact]
	public void InitializationFailurePropagatesAndPreventsRetryOrDispatch()
	{
		var expected = new ApplicationException("initialization");
		var participant = new Participant { Initializing = () => throw expected };
		using var setup = new LifecycleSetup([participant]);
		var lifecycle = setup.Lifecycle;
		Assert.Same(expected, Assert.Throws<ApplicationException>(() => lifecycle.Initialize()));
		Assert.Throws<InvalidOperationException>(() => lifecycle.Initialize());
		Assert.Throws<InvalidOperationException>(() => lifecycle.Tick(0.1));
		Assert.Equal(0, participant.Disposals);
	}

	[Fact]
	public void TickFailurePropagatesAndDoesNotLeaveDispatchLocked()
	{
		var expected = new ApplicationException("tick");
		var participant = new Participant { Ticking = () => throw expected };
		using var setup = new LifecycleSetup([participant]);
		var lifecycle = setup.Lifecycle;
		lifecycle.Initialize();
		Assert.Same(expected, Assert.Throws<ApplicationException>(() => lifecycle.Tick(0.1)));
		participant.Ticking = null;
		lifecycle.Tick(0.2);
		Assert.Equal(1, participant.Frames);
	}

	[Fact]
	public void DispatchFailureStopsCurrentPhaseRegardlessOfParticipantOrder()
	{
		var calls = 0;
		var expected = new ApplicationException("tick");
		Action fail = () =>
		{
			calls++;
			throw expected;
		};
		using var setup = new LifecycleSetup([
			new Participant { Ticking = fail }, new Participant { Ticking = fail }
		]);
		setup.Lifecycle.Initialize();
		Assert.Same(expected, Assert.Throws<ApplicationException>(() => setup.Lifecycle.Tick(0.1)));
		Assert.Equal(1, calls);
	}

	[Fact]
	public void ReentryAndDisposalDuringInitializationAreRejected()
	{
		InjectraLifecycle? lifecycle = null;
		var participant = new Participant
		{
			Initializing = () =>
			{
				Assert.Throws<InvalidOperationException>(() => lifecycle!.Initialize());
				Assert.Throws<InvalidOperationException>(() => lifecycle!.Tick(0.1));
				Assert.Throws<InvalidOperationException>(() => lifecycle!.Dispose());
			}
		};
		using var setup = new LifecycleSetup([participant]);
		lifecycle = setup.Lifecycle;
		lifecycle.Initialize();
		lifecycle.Tick(0.1);
		Assert.Equal(1, participant.Frames);
	}

	[Fact]
	public void CrossPhaseReentryAndDisposalDuringDispatchAreRejected()
	{
		InjectraLifecycle? lifecycle = null;
		var participant = new Participant
		{
			Ticking = () =>
			{
				Assert.Throws<InvalidOperationException>(() => lifecycle!.Tick(0.1));
				Assert.Throws<InvalidOperationException>(() => lifecycle!.Dispatch<IPhysicsTick, double>(0.02));
				Assert.Throws<InvalidOperationException>(() => lifecycle!.Dispose());
			}
		};
		using var setup = new LifecycleSetup([participant]);
		lifecycle = setup.Lifecycle;
		lifecycle.Initialize();
		lifecycle.Tick(0.1);
		lifecycle.Dispatch<IPhysicsTick, double>(0.02);
		Assert.Equal(1, participant.PhysicsCalls);
	}

	[Fact]
	public void EmptyRegisteredPhaseStillValidatesItsArgumentAndCanRun()
	{
		var builder = new ContainerBuilder().UseLifecycle(l =>
			l.RegisterPhase<IPhysicsTick, double>(static (p, dt) => p.PhysicsTick(dt)));
		using var container = builder.Build();
		var lifecycle = container.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		lifecycle.Dispatch<IPhysicsTick, double>(0.02);
		Assert.Throws<InvalidOperationException>(() => lifecycle.Dispatch<IPhysicsTick, float>(0.02f));
		Assert.Throws<InvalidOperationException>(() => lifecycle.Dispatch<IUnknown, double>(0.02));
	}

	[Fact]
	public void InvalidAndLatePhaseRegistrationsAreRejected()
	{
		Assert.Throws<ArgumentNullException>(() => BuildWith(l => l.RegisterPhase<IPhysicsTick, double>(null!)));
		Assert.Throws<ArgumentException>(() => BuildWith(l => l.RegisterPhase<object, double>(static (_, _) => { })));
		Assert.Throws<ArgumentException>(() => BuildWith(l => l.RegisterPhase<IInitializable, double>(static (p, _) => p.Initialize())));
		Assert.Throws<InvalidOperationException>(() => BuildWith(l => l.RegisterPhase<IFrameTickable, double>(static (p, dt) => p.FrameTick(dt))));
		using var setup = new LifecycleSetup([]);
		Assert.Throws<InvalidOperationException>(() => setup.Lifecycle.RegisterPhase<IUnknown, double>(static (_, _) => { }));
	}

	[Fact]
	public void DisposeStopsAllOperationsWithoutDisposingParticipants()
	{
		var participant = new Participant();
		using var setup = new LifecycleSetup([participant]);
		var lifecycle = setup.Lifecycle;
		lifecycle.Initialize();
		lifecycle.Dispose();
		lifecycle.Dispose();
		Assert.Equal(0, participant.Disposals);
		Assert.Throws<ObjectDisposedException>(() => lifecycle.Initialize());
		Assert.Throws<ObjectDisposedException>(() => lifecycle.Tick(0.1));
		Assert.Throws<ObjectDisposedException>(() => lifecycle.Dispatch<IPhysicsTick, double>(0.02));
		Assert.Throws<ObjectDisposedException>(() => lifecycle.RegisterPhase<IPhysicsTick, double>(static (p, dt) => p.PhysicsTick(dt)));
	}

	[Fact]
	public void DistinctObjectsWithValueEqualityAreNotDeduplicated()
	{
		var first = new EqualParticipant();
		var second = new EqualParticipant();
		using var setup = new LifecycleSetup([first, second]);
		setup.Lifecycle.Initialize();
		setup.Lifecycle.Tick(0.1);
		Assert.Equal(1, first.Frames);
		Assert.Equal(1, second.Frames);
	}

	[Fact]
	public void DuplicateInstanceIsRejectedBeforeInitialization()
	{
		var participant = new Participant();
		Assert.Throws<RegistrationException>(() => new LifecycleSetup([participant, participant]));
		Assert.Equal(0, participant.Initializations);
		Assert.Equal(0, participant.Frames);
	}

	[Fact]
	public void LifecycleHasNoPublicConstructors()
	{
		Assert.Empty(typeof(InjectraLifecycle).GetConstructors());
	}

	[Fact]
	public void DisposedOwnerAndManagerReleaseParticipantReferences()
	{
		var (lifecycle, weak) = CreateDisposedManager();
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
		Assert.False(weak.IsAlive);
		GC.KeepAlive(lifecycle);
	}

	[Fact]
	public void WarmDispatchDoesNotAllocate()
	{
		using var setup = new LifecycleSetup([new Participant()]);
		var lifecycle = setup.Lifecycle;
		lifecycle.Initialize();
		for (var i = 0; i < 1024; i++)
		{
			lifecycle.Tick(0.1);
			lifecycle.Dispatch<IPhysicsTick, double>(0.02);
		}
		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 1024; i++)
		{
			lifecycle.Tick(0.1);
			lifecycle.Dispatch<IPhysicsTick, double>(0.02);
		}
		Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
	}

	private static void BuildWith(Action<InjectraLifecycle> configure)
	{
		using var container = new ContainerBuilder().UseLifecycle(configure).Build();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static (InjectraLifecycle, WeakReference) CreateDisposedManager()
	{
		var participant = new Participant();
		using var setup = new LifecycleSetup([participant]);
		return (setup.Lifecycle, new WeakReference(participant));
	}

	private sealed class LifecycleSetup : IDisposable
	{
		private readonly Container _container;
		public InjectraLifecycle Lifecycle => _container.Resolve<InjectraLifecycle>();

		public LifecycleSetup(object[] participants)
		{
			var builder = new ContainerBuilder().UseLifecycle(l =>
				l.RegisterPhase<IPhysicsTick, double>(static (p, dt) => p.PhysicsTick(dt)));
			foreach (var participant in participants)
			{
				if (participant is IInitializable initializable) builder.RegisterInstance(initializable);
				if (participant is IFrameTickable frame) builder.RegisterInstance(frame);
				if (participant is IPhysicsTick physics) builder.RegisterInstance(physics);
			}
			_container = builder.Build();
		}

		public void Dispose() => _container.Dispose();
	}

	private interface IPhysicsTick
	{
		void PhysicsTick(double deltaTime);
	}
	private interface IUnknown;
	private sealed class Participant : IInitializable, IFrameTickable, IPhysicsTick, IDisposable
	{
		public Action? Initializing;
		public Action? Ticking;
		public int Initializations, Frames, PhysicsCalls, Disposals;
		public double FrameDelta, PhysicsDelta;

		public void Initialize()
		{
			Initializing?.Invoke();
			Initializations++;
		}

		public void FrameTick(double dt)
		{
			Ticking?.Invoke();
			Frames++;
			FrameDelta = dt;
		}

		public void PhysicsTick(double dt)
		{
			PhysicsCalls++;
			PhysicsDelta = dt;
		}

		public void Dispose() => Disposals++;
	}
	private sealed class EqualParticipant : IFrameTickable
	{
		public int Frames;
		public void FrameTick(double _) => Frames++;
		public override bool Equals(object? obj) => obj is EqualParticipant;
		public override int GetHashCode() => 0;
	}
}
