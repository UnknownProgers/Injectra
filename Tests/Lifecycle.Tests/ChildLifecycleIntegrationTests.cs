using Xunit;
using static Injectra.Lifecycle.Tests.RootLifecycleIntegrationTests;

namespace Injectra.Lifecycle.Tests;

public sealed class ChildLifecycleIntegrationTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ChildrenJoinTheRootManagerAndInitializeOnce(bool initializeFirst)
	{
		var parentState = new State();
		var childState = new State();
		var builder = new ContainerBuilder().UseLifecycle();
		builder.Register<Session>(_ => new Session(parentState), Lifetime.Singleton).AsImplementedInterfaces();
		using var root = builder.Build();
		var lifecycle = root.Resolve<InjectraLifecycle>();
		if (initializeFirst) lifecycle.Initialize();
		using var child = root.CreateChild(b =>
			b.Register<Session>(_ => new Session(childState), Lifetime.Singleton).AsImplementedInterfaces());
		Assert.Same(lifecycle, child.Resolve<InjectraLifecycle>());
		Assert.Equal(initializeFirst ? 1 : 0, childState.Initializations);
		if (!initializeFirst) lifecycle.Initialize();
		using var empty = child.CreateChild();
		lifecycle.Tick(0.1);
		Assert.Equal(1, parentState.Initializations);
		Assert.Equal(1, childState.Initializations);
		Assert.Equal(1, parentState.Ticks);
		Assert.Equal(1, childState.Ticks);
		child.Dispose();
		lifecycle.Tick(0.1);
		Assert.Equal(2, parentState.Ticks);
		Assert.Equal(1, childState.Ticks);
		Assert.Equal(1, childState.Disposals);
	}

	[Fact]
	public void GrandchildrenParticipateInCustomPhasesWithoutRepeatedSetup()
	{
		var state = new State();
		var builder = new ContainerBuilder().UseLifecycle(lifecycle =>
			lifecycle.RegisterPhase<IPhysics, double>(static (p, dt) => p.Physics(dt)));
		using var root = builder.Build();
		using var child = root.CreateChild();
		using var grandchild = child.CreateChild(b =>
			b.Register<Session>(_ => new Session(state), Lifetime.Singleton).AsImplementedInterfaces());
		var lifecycle = root.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		lifecycle.Dispatch<IPhysics, double>(0.02);
		Assert.Equal(1, state.Initializations);
		Assert.Equal(1, state.PhysicsCalls);
		child.Dispose();
		lifecycle.Dispatch<IPhysics, double>(0.02);
		Assert.Equal(1, state.PhysicsCalls);
		Assert.Equal(1, state.Disposals);
	}

	[Fact]
	public void FailedChildInitializationCleansTheChildAndLeavesTheRootUsable()
	{
		var expected = new ApplicationException("child initialization");
		var state = new State { OnInitialize = () => throw expected };
		using var root = new ContainerBuilder().UseLifecycle().Build();
		var lifecycle = root.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		Assert.Same(expected, Assert.Throws<ApplicationException>(() => root.CreateChild(b =>
			b.Register<Session>(_ => new Session(state), Lifetime.Singleton).AsImplementedInterfaces())));
		Assert.Equal(1, state.Disposals);
		lifecycle.Tick(0.1);
		Assert.Equal(0, state.Ticks);
		using var next = root.CreateChild();
	}

	[Fact]
	public void ChildTransientFailsBeforeCreation()
	{
		var state = new State();
		using var root = new ContainerBuilder().UseLifecycle().Build();
		Assert.Throws<RegistrationException>(() => root.CreateChild(b =>
			b.Register<IFrameTickable>(_ => new Session(state), Lifetime.Transient)));
		Assert.Equal(0, state.Constructions);
	}

	[Fact]
	public void ParentInstanceCannotBeAttachedAgainThroughAChild()
	{
		var state = new State();
		var participant = new Session(state);
		var builder = new ContainerBuilder().UseLifecycle();
		builder.RegisterInstance<IFrameTickable>(participant);
		using var root = builder.Build();
		Assert.Throws<RegistrationException>(() => root.CreateChild(b =>
			b.RegisterInstance<IFrameTickable>(participant)));
		var lifecycle = root.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		lifecycle.Tick(0.1);
		Assert.Equal(1, state.Ticks);
		Assert.Equal(0, state.Disposals);
	}

	[Fact]
	public void ChildDisposalDuringDispatchIsSynchronousAndStopsFutureTicks()
	{
		var state = new State();
		using var root = new ContainerBuilder().UseLifecycle().Build();
		using var child = root.CreateChild(b =>
			b.Register<Session>(_ => new Session(state), Lifetime.Singleton).AsImplementedInterfaces());
		state.OnTick = () =>
		{
			child.Dispose();
			child.Dispose();
			Assert.Equal(1, state.Disposals);
			Assert.Throws<ObjectDisposedException>(() => child.Resolve<IFrameTickable>());
			Assert.Throws<ObjectDisposedException>(() => child.CreateChild());
		};
		var lifecycle = root.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		lifecycle.Tick(0.1);
		Assert.Equal(1, state.Ticks);
		Assert.Equal(1, state.Disposals);
		lifecycle.Tick(0.1);
		Assert.Equal(1, state.Ticks);
	}

	[Fact]
	public void ChildCreatedDuringTickJoinsAfterTheCurrentPhase()
	{
		var state = new State();
		var builder = new ContainerBuilder().UseLifecycle();
		var creator = new State();
		builder.Register<IFrameTickable>(_ => new Session(creator), Lifetime.Singleton);
		using var root = builder.Build();
		var lifecycle = root.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		creator.OnTick = () =>
		{
			creator.OnTick = null;
			var child = root.CreateChild(b =>
				b.Register<Session>(_ => new Session(state), Lifetime.Singleton).AsImplementedInterfaces());
			Assert.Same(lifecycle, child.Resolve<InjectraLifecycle>());
			Assert.Equal(0, state.Initializations);
		};
		lifecycle.Tick(0.1);
		Assert.Equal(1, state.Initializations);
		Assert.Equal(0, state.Ticks);
		lifecycle.Tick(0.1);
		Assert.Equal(1, state.Ticks);
	}

	[Fact]
	public void ChildCreatedByAChildTickInitializesEvenWhenTheTickThrows()
	{
		var expected = new ApplicationException("tick");
		var creator = new State();
		var state = new State();
		using var root = new ContainerBuilder().UseLifecycle().Build();
		using var level = root.CreateChild(b =>
			b.Register<IFrameTickable>(_ => new Session(creator), Lifetime.Singleton));
		creator.OnTick = () =>
		{
			creator.OnTick = null;
			level.CreateChild(b =>
				b.Register<Session>(_ => new Session(state), Lifetime.Singleton).AsImplementedInterfaces());
			throw expected;
		};
		var lifecycle = root.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		Assert.Same(expected, Assert.Throws<ApplicationException>(() => lifecycle.Tick(0.1)));
		Assert.Equal(1, state.Initializations);
		Assert.Equal(0, state.Ticks);
		lifecycle.Tick(0.1);
		Assert.Equal(1, state.Ticks);
	}

	[Fact]
	public void ChildClosedBeforeDeferredAttachmentIsSkipped()
	{
		var state = new State();
		var creator = new State();
		var builder = new ContainerBuilder().UseLifecycle();
		builder.Register<IFrameTickable>(_ => new Session(creator), Lifetime.Singleton);
		using var root = builder.Build();
		creator.OnTick = () =>
		{
			using var child = root.CreateChild(b =>
				b.Register<Session>(_ => new Session(state), Lifetime.Singleton).AsImplementedInterfaces());
		};
		var lifecycle = root.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		lifecycle.Tick(0.1);
		Assert.Equal(0, state.Constructions);
		Assert.Equal(0, state.Initializations);
	}

	[Fact]
	public void DeferredFailureCleansChildAndPreservesTheTickError()
	{
		var tickError = new ApplicationException("tick");
		var initError = new ApplicationException("initialize");
		var state = new State { OnInitialize = () => throw initError };
		var creator = new State();
		var builder = new ContainerBuilder().UseLifecycle();
		builder.Register<IFrameTickable>(_ => new Session(creator), Lifetime.Singleton);
		using var root = builder.Build();
		creator.OnTick = () =>
		{
			creator.OnTick = null;
			root.CreateChild(b =>
				b.Register<Session>(_ => new Session(state), Lifetime.Singleton).AsImplementedInterfaces());
			throw tickError;
		};
		var lifecycle = root.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		var error = Assert.Throws<AggregateException>(() => lifecycle.Tick(0.1));
		Assert.Contains(tickError, error.InnerExceptions);
		Assert.Contains(initError, error.InnerExceptions);
		Assert.Equal(1, state.Disposals);
		lifecycle.Tick(0.1);
		Assert.Equal(0, state.Ticks);
	}

	[Fact]
	public void ChildrenCreatedDuringInitializationAreInitializedBeforeTheFirstTick()
	{
		var creator = new State();
		var state = new State();
		var nested = new State();
		var builder = new ContainerBuilder().UseLifecycle();
		builder.Register<IInitializable>(_ => new Session(creator), Lifetime.Singleton);
		using var root = builder.Build();
		creator.OnInitialize = () => root.CreateChild(b =>
			b.Register<Session>(_ => new Session(state), Lifetime.Singleton).AsImplementedInterfaces());
		state.OnInitialize = () => root.CreateChild(b =>
			b.Register<Session>(_ => new Session(nested), Lifetime.Singleton).AsImplementedInterfaces());
		var lifecycle = root.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		Assert.Equal(1, state.Initializations);
		Assert.Equal(1, nested.Initializations);
		lifecycle.Tick(0.1);
		Assert.Equal(1, state.Ticks);
		Assert.Equal(1, nested.Ticks);
	}

	[Fact]
	public void WarmDispatchWithChildrenDoesNotAllocate()
	{
		using var root = new ContainerBuilder().UseLifecycle().Build();
		using var child = root.CreateChild(b =>
			b.Register<IFrameTickable>(_ => new Session(new State()), Lifetime.Singleton));
		var lifecycle = root.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		for (var i = 0; i < 1024; i++) lifecycle.Tick(0.1);
		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 1024; i++) lifecycle.Tick(0.1);
		Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
	}
}
