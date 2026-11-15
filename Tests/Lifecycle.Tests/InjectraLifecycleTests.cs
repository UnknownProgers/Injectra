using System.Runtime.CompilerServices;
using Xunit;

namespace Injectra.Lifecycle.Tests;

public sealed class InjectraLifecycleTests
{
	[Fact]
	public void InitializationIsExplicitAndRunsOnce()
	{
		var participant = new Participant();
		using var lifecycle = new InjectraLifecycle([participant]);
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
		using var lifecycle = new InjectraLifecycle([participant, new object()])
			.RegisterPhase<IPhysicsTick, double>(static (service, delta) => service.PhysicsTick(delta));

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
		using var lifecycle = new InjectraLifecycle([first, second]);
		lifecycle.Initialize();
		lifecycle.Tick(0.1);
		Assert.Equal(1, first.Frames);
		Assert.Equal(1, second.Frames);
	}

	[Fact]
	public void InitializationFailurePropagatesAndPreventsRetryOrDispatch()
	{
		var expected = new ApplicationException("initialization");
		var participant = new Participant { Initializing = () => throw expected };
		using var lifecycle = new InjectraLifecycle([participant]);
		Assert.Same(expected, Assert.Throws<ApplicationException>(() => lifecycle.Initialize()));
		Assert.Throws<InvalidOperationException>(() => lifecycle.Initialize());
		Assert.Throws<InvalidOperationException>(() => lifecycle.Tick(0.1));
		Assert.Throws<InvalidOperationException>(() => lifecycle.RegisterPhase<IPhysicsTick, double>(static (p, dt) => p.PhysicsTick(dt)));
		Assert.Equal(0, participant.Disposals);
	}

	[Fact]
	public void TickFailurePropagatesAndDoesNotLeaveDispatchLocked()
	{
		var expected = new ApplicationException("tick");
		var participant = new Participant { Ticking = () => throw expected };
		using var lifecycle = new InjectraLifecycle([participant]);
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
		Action fail = () => { calls++; throw expected; };
		using var lifecycle = new InjectraLifecycle([
			new Participant { Ticking = fail },
			new Participant { Ticking = fail }
		]);
		lifecycle.Initialize();
		Assert.Same(expected, Assert.Throws<ApplicationException>(() => lifecycle.Tick(0.1)));
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
		using var owned = lifecycle = new InjectraLifecycle([participant]);
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
		using var owned = lifecycle = new InjectraLifecycle([participant])
			.RegisterPhase<IPhysicsTick, double>(static (p, dt) => p.PhysicsTick(dt));
		lifecycle.Initialize();
		lifecycle.Tick(0.1);
		lifecycle.Dispatch<IPhysicsTick, double>(0.02);
		Assert.Equal(1, participant.PhysicsCalls);
	}

	[Fact]
	public void InvalidAndLatePhaseRegistrationsAreRejected()
	{
		using var lifecycle = new InjectraLifecycle([]);
		Assert.Throws<ArgumentNullException>(() => lifecycle.RegisterPhase<IPhysicsTick, double>(null!));
		Assert.Throws<ArgumentException>(() => lifecycle.RegisterPhase<object, double>(static (_, _) => { }));
		Assert.Throws<ArgumentException>(() => lifecycle.RegisterPhase<IInitializable, double>(static (p, _) => p.Initialize()));
		Assert.Throws<InvalidOperationException>(() => lifecycle.RegisterPhase<IFrameTick, double>(static (p, dt) => p.FrameTick(dt)));
		lifecycle.Initialize();
		Assert.Throws<InvalidOperationException>(() => lifecycle.RegisterPhase<IPhysicsTick, double>(static (p, dt) => p.PhysicsTick(dt)));
		lifecycle.Tick(0.1);
	}

	[Fact]
	public void DisposeStopsAllOperationsWithoutDisposingParticipants()
	{
		var participant = new Participant();
		var lifecycle = new InjectraLifecycle([participant]);
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
		using var lifecycle = new InjectraLifecycle([first, second]);
		lifecycle.Initialize();
		lifecycle.Tick(0.1);
		Assert.Equal(1, first.Frames);
		Assert.Equal(1, second.Frames);
	}

	[Fact]
	public void DuplicateInstanceIsRejectedBeforeInitialization()
	{
		var participant = new Participant();
		var error = Assert.Throws<ArgumentException>(() => new InjectraLifecycle([participant, participant]));
		Assert.Equal("participants", error.ParamName);
		Assert.Equal(0, participant.Initializations);
		Assert.Equal(0, participant.Frames);
	}

	[Fact]
	public void NullParticipantsAreRejected()
	{
		Assert.Throws<ArgumentNullException>(() => new InjectraLifecycle(null!));
		Assert.Throws<ArgumentException>(() => new InjectraLifecycle([new object(), null!]));
	}

	[Fact]
	public void DisposeReleasesParticipantReferences()
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
		using var lifecycle = new InjectraLifecycle([new Participant()])
			.RegisterPhase<IPhysicsTick, double>(static (p, dt) => p.PhysicsTick(dt));
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

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static (InjectraLifecycle, WeakReference) CreateDisposedManager()
	{
		var participant = new Participant();
		var lifecycle = new InjectraLifecycle([participant]);
		lifecycle.Dispose();
		return (lifecycle, new WeakReference(participant));
	}

	private interface IPhysicsTick { void PhysicsTick(double deltaTime); }
	private interface IUnknown;
	private sealed class Participant : IInitializable, IFrameTick, IPhysicsTick, IDisposable
	{
		public Action? Initializing;
		public Action? Ticking;
		public int Initializations, Frames, PhysicsCalls, Disposals;
		public double FrameDelta, PhysicsDelta;
		public void Initialize() { Initializing?.Invoke(); Initializations++; }
		public void FrameTick(double dt) { Ticking?.Invoke(); Frames++; FrameDelta = dt; }
		public void PhysicsTick(double dt) { PhysicsCalls++; PhysicsDelta = dt; }
		public void Dispose() => Disposals++;
	}
	private sealed class EqualParticipant : IFrameTick
	{
		public int Frames;
		public void FrameTick(double _) => Frames++;
		public override bool Equals(object? obj) => obj is EqualParticipant;
		public override int GetHashCode() => 0;
	}
}
