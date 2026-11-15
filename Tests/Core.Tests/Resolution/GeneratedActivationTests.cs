using Injectra.Generation;
using Xunit;

namespace Injectra.Core.Tests.Resolution;

public sealed class GeneratedActivationTests
{
	[Fact]
	public void GeneratedTransientUsesChildOverridesAndDoesNotCaptureTheRoot()
	{
		var dependencyTypes = new[] { typeof(IClock) };
		GeneratedConstructors.Register(typeof(ManualTransient), dependencyTypes,
			context => new ManualTransient(context.Resolve<IClock>(0)) { Generated = true });
		// The descriptor must own its metadata; caller mutations cannot change a prepared graph.
		dependencyTypes[0] = typeof(string);
		var rootClock = new Clock();
		var childClock = new Clock();
		var builder = new ContainerBuilder();
		builder.RegisterInstance<IClock>(rootClock);
		builder.Register<ManualTransient>(Lifetime.Transient);
		using var root = builder.Build();
		using var child = root.CreateChild(b => b.RegisterInstance<IClock>(childClock));

		var first = child.Resolve<ManualTransient>();
		Assert.True(first.Generated);
		Assert.Same(childClock, first.Clock);
		Assert.Same(rootClock, root.Resolve<ManualTransient>().Clock);
		Assert.NotSame(first, child.Resolve<ManualTransient>());
	}

	[Fact]
	public void GeneratedSingletonIsLazySharedAcrossAliasesAndDisposedByItsOwner()
	{
		var calls = 0;
		GeneratedConstructors.Register(typeof(ManualSingleton), [typeof(IClock)], context =>
		{
			calls++;
			return new ManualSingleton(context.Resolve<IClock>(0));
		});
		var clock = new Clock();
		var builder = new ContainerBuilder();
		builder.RegisterInstance<IClock>(clock);
		builder.Register<ManualSingleton>(Lifetime.Singleton).AsSelf().As<IDisposable>();
		using var root = builder.Build();
		Assert.Equal(0, calls);
		using var child = root.CreateChild(b => b.RegisterInstance<IClock>(new Clock()));

		var singleton = child.Resolve<ManualSingleton>();
		Assert.Same(clock, singleton.Clock);
		Assert.Same(singleton, root.Resolve<IDisposable>());
		Assert.Equal(1, calls);
		child.Dispose();
		Assert.Equal(0, singleton.Disposals);
		root.Dispose();
		Assert.Equal(1, singleton.Disposals);
	}

	[Fact]
	public void GeneratedConstructorFailureIsNotCachedOrWrapped()
	{
		var calls = 0;
		var error = new ApplicationException("Constructor failure");
		GeneratedConstructors.Register(typeof(ManualRetry), [], _ => ++calls == 1 ? throw error : new ManualRetry());
		var builder = new ContainerBuilder();
		builder.Register<ManualRetry>(Lifetime.Singleton);
		using var container = builder.Build();

		Assert.Same(error, Assert.Throws<ApplicationException>(() => container.Resolve<ManualRetry>()));
		Assert.Same(container.Resolve<ManualRetry>(), container.Resolve<ManualRetry>());
		Assert.Equal(2, calls);
	}

	[Fact]
	public void CachedAncestorDependencyCannotBeUsedWhileOwnerIsDisposing()
	{
		GeneratedConstructors.Register(typeof(DisposalConsumer), [typeof(IClock)],
			context => new DisposalConsumer(context.Resolve<IClock>(0)));
		var builder = new ContainerBuilder();
		builder.Register<Clock>(Lifetime.Singleton).As<IClock>();
		builder.Register<DisposalConsumer>(Lifetime.Transient);
		using var root = builder.Build();
		using var waiting = root.CreateChild();
		Assert.Same(root.Resolve<IClock>(), waiting.Resolve<DisposalConsumer>().Clock);
		Exception? observed = null;
		root.CreateChild(b => b.RegisterInstance(new DisposalCallback(() =>
			observed = Record.Exception(() => waiting.Resolve<DisposalConsumer>()))).OwnedByContainer());

		root.Dispose();

		Assert.IsType<ObjectDisposedException>(observed);
	}

	[Theory]
	[InlineData(Lifetime.Singleton)]
	[InlineData(Lifetime.Transient)]
	public void WarmedParentDoesNotCaptureChildDependencyLifetime(Lifetime childLifetime)
	{
		GeneratedConstructors.Register(typeof(LifetimeConsumer), [typeof(IClock)],
			context => new LifetimeConsumer(context.Resolve<IClock>(0)));
		var builder = new ContainerBuilder();
		builder.Register<Clock>(Lifetime.Singleton).As<IClock>();
		builder.Register<LifetimeConsumer>(Lifetime.Transient);
		using var root = builder.Build();
		var parentClock = root.Resolve<LifetimeConsumer>().Clock;
		using var child = root.CreateChild(b => b.Register<Clock>(childLifetime).As<IClock>());

		var first = child.Resolve<LifetimeConsumer>().Clock;
		var second = child.Resolve<LifetimeConsumer>().Clock;

		Assert.NotSame(parentClock, first);
		Assert.Equal(childLifetime == Lifetime.Singleton, ReferenceEquals(first, second));
	}

	[Fact]
	public void GeneratedParentRegistrationDoesNotBypassChildFactory()
	{
		GeneratedConstructors.Register(typeof(FactoryOverride), [], _ => new FactoryOverride());
		var builder = new ContainerBuilder();
		builder.Register<FactoryOverride>(Lifetime.Transient);
		using var root = builder.Build();
		root.Resolve<FactoryOverride>();
		var expected = new FactoryOverride();
		var calls = 0;
		using var child = root.CreateChild(b => b.Register(_ =>
		{
			calls++;
			return expected;
		}, Lifetime.Transient));

		Assert.Same(expected, child.Resolve<FactoryOverride>());
		Assert.Same(expected, child.Resolve<FactoryOverride>());
		Assert.Equal(2, calls);
	}

	private sealed class FactoryOverride;

	private sealed class LifetimeConsumer(IClock clock)
	{
		public IClock Clock { get; } = clock;
	}

	private sealed class DisposalConsumer(IClock clock)
	{
		public IClock Clock { get; } = clock;
	}
	private sealed class DisposalCallback(Action callback) : IDisposable
	{
		public void Dispose() => callback();
	}

	private interface IClock;
	private sealed class Clock : IClock;
	private sealed class ManualTransient(IClock clock)
	{
		public IClock Clock { get; } = clock;
		public bool Generated { get; init; }
	}
	private sealed class ManualSingleton(IClock clock) : IDisposable
	{
		public IClock Clock { get; } = clock;
		public int Disposals { get; private set; }
		public void Dispose() => Disposals++;
	}
	private sealed class ManualRetry;
}
