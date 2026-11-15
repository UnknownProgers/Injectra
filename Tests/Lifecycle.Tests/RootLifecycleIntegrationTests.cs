using Xunit;

namespace Injectra.Lifecycle.Tests;

public sealed class RootLifecycleIntegrationTests
{
	[Fact]
	public void FrameOnlyAndCustomOnlyConstructorsAreResolvedDuringBuildWithoutInitializables()
	{
		var state = new State();
		var builder = new ContainerBuilder().UseLifecycle(lifecycle =>
			lifecycle.RegisterPhase<IPhysics, double>(static (p, dt) => p.Physics(dt)));
		builder.RegisterInstance(state);
		builder.Register<FrameOnly>(Lifetime.Singleton).As<IFrameTickable>();
		builder.Register<PhysicsOnly>(Lifetime.Singleton).As<IPhysics>();

		using var container = builder.Build();

		Assert.Equal(2, state.Constructions);
		Assert.Empty(container.ResolveAll<IInitializable>());
		var lifecycle = container.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		lifecycle.Tick(0.1);
		lifecycle.Dispatch<IPhysics, double>(0.02);
		Assert.Equal(1, state.Ticks);
		Assert.Equal(1, state.PhysicsCalls);
	}

	[Fact]
	public void ExplicitFrameContractDoesNotEnableOtherImplementedInterfaces()
	{
		var state = new State();
		var builder = new ContainerBuilder().UseLifecycle(lifecycle =>
			lifecycle.RegisterPhase<IPhysics, double>(static (p, dt) => p.Physics(dt)));
		builder.Register<Session>(_ => new Session(state), Lifetime.Singleton).As<IFrameTickable>();
		using var container = builder.Build();
		Assert.Equal(1, state.Constructions);
		var lifecycle = container.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		lifecycle.Tick(0.1);
		lifecycle.Dispatch<IPhysics, double>(0.02);
		Assert.Equal(0, state.Initializations);
		Assert.Equal(1, state.Ticks);
		Assert.Equal(0, state.PhysicsCalls);
	}

	[Fact]
	public void ExplicitInitializationContractDoesNotEnableFrames()
	{
		var state = new State();
		var builder = new ContainerBuilder().UseLifecycle();
		builder.Register<IInitializable>(_ => new Session(state), Lifetime.Singleton);
		using var container = builder.Build();
		Assert.Equal(0, state.Initializations);
		var lifecycle = container.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		lifecycle.Tick(0.1);
		Assert.Equal(1, state.Initializations);
		Assert.Equal(0, state.Ticks);
	}

	[Fact]
	public void ImplementedInterfacesConnectEveryServiceOnceAndContainerDisposesThemOnce()
	{
		var first = new State();
		var second = new State();
		var builder = new ContainerBuilder().UseLifecycle(lifecycle =>
			lifecycle.RegisterPhase<IPhysics, double>(static (p, dt) => p.Physics(dt)));
		builder.Register<Session>(_ => new Session(first), Lifetime.Singleton).AsImplementedInterfaces();
		builder.Register<Session>(_ => new Session(second), Lifetime.Singleton).AsImplementedInterfaces();
		var container = builder.Build();
		var lifecycle = container.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		lifecycle.Tick(0.1);
		lifecycle.Dispatch<IPhysics, double>(0.02);
		foreach (var state in new[] { first, second })
		{
			Assert.Equal(1, state.Constructions);
			Assert.Equal(1, state.Initializations);
			Assert.Equal(1, state.Ticks);
			Assert.Equal(1, state.PhysicsCalls);
		}
		container.Dispose();
		Assert.Equal(1, first.Disposals);
		Assert.Equal(1, second.Disposals);
		Assert.Throws<ObjectDisposedException>(() => lifecycle.Tick(0.1));
	}

	[Fact]
	public void SelfOnlyRegistrationStaysLazyAndDoesNotParticipateAfterManualResolution()
	{
		var state = new State();
		var builder = new ContainerBuilder().UseLifecycle();
		builder.RegisterInstance(state);
		builder.Register<Session>(Lifetime.Singleton);
		using var container = builder.Build();
		Assert.Equal(0, state.Constructions);
		container.Resolve<Session>();
		var lifecycle = container.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		lifecycle.Tick(0.1);
		Assert.Equal(0, state.Initializations);
		Assert.Equal(0, state.Ticks);
	}

	[Fact]
	public void ExternalInstanceUnderDifferentInterfacesRemainsExternallyOwned()
	{
		var state = new State();
		var instance = new Session(state);
		var builder = new ContainerBuilder().UseLifecycle();
		builder.RegisterInstance<IInitializable>(instance);
		builder.RegisterInstance<IFrameTickable>(instance);
		using var container = builder.Build();
		var lifecycle = container.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		lifecycle.Tick(0.1);
		container.Dispose();
		Assert.Equal(1, state.Initializations);
		Assert.Equal(1, state.Ticks);
		Assert.Equal(0, state.Disposals);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void TransientPhaseRegistrationFailsBeforeParticipantCreation(bool custom)
	{
		var state = new State();
		var builder = new ContainerBuilder().UseLifecycle(lifecycle =>
			lifecycle.RegisterPhase<IPhysics, double>(static (p, dt) => p.Physics(dt)));
		if (custom)
			builder.Register<IPhysics>(_ => new Session(state), Lifetime.Transient);
		else
			builder.Register<IFrameTickable>(_ => new Session(state), Lifetime.Transient);
		Assert.Throws<RegistrationException>(() => builder.Build());
		Assert.Equal(0, state.Constructions);
	}

	[Fact]
	public void SameInstanceCannotParticipateTwiceInTheSamePhase()
	{
		var instance = new Session(new State());
		var builder = new ContainerBuilder().UseLifecycle();
		builder.RegisterInstance<IFrameTickable>(instance);
		builder.RegisterInstance<IFrameTickable>(instance);
		Assert.Throws<RegistrationException>(() => builder.Build());
	}

	[Fact]
	public void ConfigurationErrorCleansCreatedParticipantsAndKeepsOriginalException()
	{
		var state = new State();
		var expected = new ApplicationException("configuration");
		var builder = new ContainerBuilder().UseLifecycle(_ => throw expected);
		builder.Register<IFrameTickable>(_ => new Session(state), Lifetime.Singleton);
		Assert.Same(expected, Assert.Throws<ApplicationException>(() => builder.Build()));
		Assert.Equal(1, state.Disposals);
	}

	[Fact]
	public void InitializationFailureLeavesCleanupWithTheContainerOwner()
	{
		var expected = new ApplicationException("initialize");
		var state = new State { OnInitialize = () => throw expected };
		var builder = new ContainerBuilder().UseLifecycle();
		builder.Register<Session>(_ => new Session(state), Lifetime.Singleton).AsImplementedInterfaces();
		using var container = builder.Build();
		var lifecycle = container.Resolve<InjectraLifecycle>();
		Assert.Same(expected, Assert.Throws<ApplicationException>(() => lifecycle.Initialize()));
		Assert.Throws<InvalidOperationException>(() => lifecycle.Tick(0.1));
		container.Dispose();
		Assert.Equal(1, state.Disposals);
	}

	[Fact]
	public void ContainerDisposalInsideInitializationOrTickIsRejectedWithoutDisposingServices()
	{
		var state = new State();
		var builder = new ContainerBuilder().UseLifecycle();
		builder.Register<Session>(_ => new Session(state), Lifetime.Singleton).AsImplementedInterfaces();
		using var container = builder.Build();
		state.OnInitialize = state.OnTick = () =>
		{
			Assert.Throws<InvalidOperationException>(() => container.Dispose());
			Assert.Equal(0, state.Disposals);
		};
		var lifecycle = container.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		lifecycle.Tick(0.1);
		container.Dispose();
		Assert.Equal(1, state.Disposals);
	}

	[Fact]
	public void CustomPhaseConfigurationIsFrozenAfterBuildAndRepeatedSetupFails()
	{
		var builder = new ContainerBuilder().UseLifecycle();
		using var container = builder.Build();
		Assert.Throws<InvalidOperationException>(() => container.Resolve<InjectraLifecycle>()
			.RegisterPhase<IPhysics, double>(static (p, dt) => p.Physics(dt)));
		var duplicate = new ContainerBuilder().UseLifecycle().UseLifecycle();
		Assert.Throws<RegistrationException>(() => duplicate.Build());
		Assert.Throws<InvalidOperationException>(() => container.CreateChild(b => b.UseLifecycle()));
	}

	[Fact]
	public void WarmContainerBackedDispatchDoesNotAllocate()
	{
		var builder = new ContainerBuilder().UseLifecycle();
		builder.Register<IFrameTickable>(_ => new Session(new State()), Lifetime.Singleton);
		using var container = builder.Build();
		var lifecycle = container.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		for (var i = 0; i < 1024; i++) lifecycle.Tick(0.1);
		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 1024; i++) lifecycle.Tick(0.1);
		Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
	}

	public interface IPhysics { void Physics(double dt); }
	public sealed class FrameOnly : IFrameTickable
	{
		private readonly State _state;
		public FrameOnly(State state) { _state = state; state.Constructions++; }
		public void FrameTick(double deltaTime) => _state.Ticks++;
	}
	public sealed class PhysicsOnly : IPhysics
	{
		private readonly State _state;
		public PhysicsOnly(State state) { _state = state; state.Constructions++; }
		public void Physics(double deltaTime) => _state.PhysicsCalls++;
	}

	public sealed class State
	{
		public int Constructions, Initializations, Ticks, PhysicsCalls, Disposals;
		public Action? OnInitialize, OnTick;
	}
	public sealed class Session : IInitializable, IFrameTickable, IPhysics, IDisposable
	{
		private readonly State _state;
		public Session(State state) { _state = state; state.Constructions++; }
		public void Initialize() { _state.OnInitialize?.Invoke(); _state.Initializations++; }
		public void FrameTick(double dt) { _state.OnTick?.Invoke(); _state.Ticks++; }
		public void Physics(double dt) => _state.PhysicsCalls++;
		public void Dispose() => _state.Disposals++;
	}
}
