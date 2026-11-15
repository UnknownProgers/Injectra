using Xunit;

namespace Injectra.Core.Tests.Resolution;

public sealed class NonLazyTests
{
	[Fact]
	public void EagerSingletonAndDependenciesAreCreatedOnceAcrossAliases()
	{
		var calls = 0;
		var builder = new ContainerBuilder();
		builder.Register<Resource>(_ => { calls++; return new Resource(); }, Lifetime.Singleton)
			.As<IDisposable>().NonLazy().AsSelf().NonLazy();
		builder.Register<Other>(_ => throw new InvalidOperationException("Must remain lazy."), Lifetime.Singleton);
		Assert.Equal(0, calls);
		using var container = builder.Build();
		Assert.Equal(1, calls);
		Assert.Same(container.Resolve<Resource>(), container.Resolve<IDisposable>());
		Assert.Equal(1, calls);
		Assert.Throws<InvalidOperationException>(() => builder.Build());
	}

	[Fact]
	public void ConstructorRootsFollowRegistrationOrderWithDependenciesFirst()
	{
		var log = new List<string>();
		var builder = new ContainerBuilder();
		builder.RegisterInstance(log);
		builder.Register<Root>(Lifetime.Singleton).NonLazy();
		builder.Register<Dependency>(Lifetime.Singleton).NonLazy();
		builder.Register<Other>(Lifetime.Singleton).NonLazy();
		using var container = builder.Build();
		Assert.Equal(["dependency", "root", "other"], log);
	}

	[Fact]
	public void ValidationCompletesBeforeAnyEagerFactoryRunsAndCanBeRetried()
	{
		var calls = 0;
		var builder = new ContainerBuilder();
		builder.Register<Resource>(_ => { calls++; return new Resource(); }, Lifetime.Singleton).NonLazy();
		builder.Register<Root>(Lifetime.Singleton);
		Assert.Throws<RegistrationException>(() => builder.Build());
		Assert.Equal(0, calls);
		builder.RegisterInstance(new List<string>());
		builder.Register<Dependency>(Lifetime.Singleton);
		using var container = builder.Build();
		Assert.Equal(1, calls);
	}

	[Fact]
	public void FailureDisposesOwnedObjectsAndConsumesBuilderWithoutTouchingExternalInstance()
	{
		var external = new Resource();
		var transferred = new Resource();
		var created = new Resource();
		var error = new ApplicationException("creation");
		IResolver? leaked = null;
		var builder = new ContainerBuilder();
		builder.RegisterInstance<IDisposable>(external);
		builder.RegisterInstance(transferred).OwnedByContainer().AsSelf();
		builder.Register<OtherResource>(_ => new OtherResource(created), Lifetime.Singleton).NonLazy();
		var failing = builder.Register<Resource>(resolver =>
		{
			leaked = resolver;
			throw error;
		}, Lifetime.Singleton).NonLazy().As<IFailure>();
		Assert.Same(error, Assert.Throws<ApplicationException>(() => builder.Build()));
		Assert.Equal(1, created.Disposals);
		Assert.Equal(1, transferred.Disposals);
		Assert.Equal(0, external.Disposals);
		Assert.Throws<ObjectDisposedException>(() => leaked!.Resolve<OtherResource>());
		Assert.Throws<InvalidOperationException>(() => builder.Build());
		Assert.Throws<InvalidOperationException>(() => failing.NonLazy());
	}

	[Fact]
	public void CleanupErrorsDoNotHideActivationError()
	{
		var activationError = new ApplicationException("activation");
		var cleanupError = new ApplicationException("cleanup");
		var builder = new ContainerBuilder();
		builder.Register<Resource>(_ => new Resource(() => throw cleanupError), Lifetime.Singleton).NonLazy();
		builder.Register<Other>(_ => throw activationError, Lifetime.Singleton).NonLazy();
		var result = Assert.Throws<AggregateException>(() => builder.Build());
		Assert.Contains(activationError, result.Flatten().InnerExceptions);
		Assert.Contains(cleanupError, result.Flatten().InnerExceptions);
	}

	[Fact]
	public void ChildEagerRootUsesOverrideAndParentSingletonKeepsItsOwner()
	{
		var parentLog = new List<string>();
		var childLog = new List<string>();
		var builder = new ContainerBuilder();
		builder.RegisterInstance(parentLog);
		builder.Register<Dependency>(Lifetime.Singleton).NonLazy();
		using var parent = builder.Build();
		using var child = parent.CreateChild(b =>
		{
			b.RegisterInstance(childLog);
			b.Register<Root>(Lifetime.Singleton).NonLazy();
		});
		Assert.Equal(["dependency"], parentLog);
		Assert.Equal(["root"], childLog);
		Assert.Same(parent.Resolve<Dependency>(), child.Resolve<Dependency>());
	}

	[Fact]
	public void FailedChildIsDetachedAndCannotDisposeParentWhileResolving()
	{
		using var parent = new ContainerBuilder().Build();
		var expected = new ApplicationException();
		Assert.Same(expected, Assert.Throws<ApplicationException>(() => parent.CreateChild(b =>
			b.Register<Resource>(_ =>
			{
				Assert.Throws<InvalidOperationException>(() => parent.Dispose());
				throw expected;
			}, Lifetime.Singleton).NonLazy())));
		using var next = parent.CreateChild();
		Assert.Same(next, next.Resolve<IResolver>());
	}

	[Fact]
	public void DynamicCycleIsReportedAndFailedContainerIsDisposed()
	{
		var builder = new ContainerBuilder();
		IResolver? captured = null;
		builder.Register<Resource>(resolver =>
		{
			captured = resolver;
			return resolver.Resolve<Resource>();
		}, Lifetime.Singleton).NonLazy();
		Assert.Throws<ResolutionException>(() => builder.Build());
		Assert.Throws<ObjectDisposedException>(() => captured!.Resolve<Resource>());
	}

	[Fact]
	public void TransientAndOpenGenericAreRejectedButClosedGenericWorks()
	{
		var builder = new ContainerBuilder();
		Assert.Throws<InvalidOperationException>(() => builder.Register<Resource>(Lifetime.Transient).NonLazy());
		Assert.Throws<InvalidOperationException>(() => builder.Register(typeof(Generic<>), Lifetime.Singleton).NonLazy());
		var closed = new ContainerBuilder();
		closed.Register<Generic<int>>(Lifetime.Singleton).NonLazy();
		using var container = closed.Build();
		Assert.Same(container.Resolve<Generic<int>>(), container.Resolve<Generic<int>>());
	}

	[Fact]
	public void ExistingInstancesKeepTheirOwnershipAndCompletedRegistrationsRejectChanges()
	{
		var external = new Resource();
		var owned = new Resource();
		var builder = new ContainerBuilder();
		builder.RegisterInstance<IDisposable>(external).NonLazy();
		var registration = builder.RegisterInstance(owned).NonLazy().OwnedByContainer();
		using var container = builder.Build();
		container.Dispose();
		Assert.Equal(0, external.Disposals);
		Assert.Equal(1, owned.Disposals);
		Assert.Throws<InvalidOperationException>(() => registration.NonLazy());
	}

	public interface IFailure;
	public sealed class Resource(Action? onDispose = null) : IDisposable, IFailure
	{
		public int Disposals { get; private set; }
		public void Dispose() { Disposals++; onDispose?.Invoke(); }
	}
	public sealed class OtherResource(Resource resource) : IDisposable
	{
		public void Dispose() => resource.Dispose();
	}
	public sealed class Dependency
	{
		public Dependency(List<string> log) => log.Add("dependency");
	}
	public sealed class Root
	{
		public Root(Dependency dependency, List<string> log) => log.Add("root");
	}
	public sealed class Other
	{
		public Other(List<string> log) => log.Add("other");
	}
	public sealed class Generic<T>;
}
