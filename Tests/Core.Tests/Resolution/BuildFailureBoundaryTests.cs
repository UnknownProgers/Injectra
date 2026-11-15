using Xunit;

namespace Injectra.Core.Tests.Resolution;

public sealed class BuildFailureBoundaryTests
{
	[Fact]
	public void FailedChildDoesNotDisposeAncestorSingletonCreatedByItsEagerFactory()
	{
		var resource = new Resource();
		var calls = 0;
		var builder = new ContainerBuilder();
		builder.Register<Resource>(_ =>
		{
			calls++;
			return resource;
		}, Lifetime.Singleton);
		using var root = builder.Build();
		var expected = new ApplicationException("child creation");

		Assert.Same(expected, Assert.Throws<ApplicationException>(() => root.CreateChild(child =>
		{
			child.Register<IDisposable>(r => r.Resolve<Resource>(), Lifetime.Singleton).NonLazy();
			child.Register<Failure>(_ => throw expected, Lifetime.Singleton).NonLazy();
		})));

		Assert.Equal(1, calls);
		Assert.Equal(0, resource.Disposals);
		Assert.Same(resource, root.Resolve<Resource>());
		root.Dispose();
		Assert.Equal(1, resource.Disposals);
	}

	[Fact]
	public void FailureCleansDynamicallyCreatedChildrenBeforeLocalServicesEvenIfCleanupThrows()
	{
		var log = new List<string>();
		var activation = new ApplicationException("activation");
		var cleanup = new ApplicationException("cleanup");
		var builder = new ContainerBuilder();
		builder.Register<Resource>(_ => new Resource(() => log.Add("root")), Lifetime.Singleton).NonLazy();
		builder.Register<Failure>(resolver =>
		{
			((Container)resolver).CreateChild(child => child.Register<Resource>(_ => new Resource(() =>
			{
				log.Add("child");
				throw cleanup;
			}), Lifetime.Singleton).NonLazy());
			throw activation;
		}, Lifetime.Singleton).NonLazy();

		var error = Assert.Throws<AggregateException>(() => builder.Build());

		Assert.Equal(["child", "root"], log);
		Assert.Contains(activation, error.Flatten().InnerExceptions);
		Assert.Contains(cleanup, error.Flatten().InnerExceptions);
	}

	[Fact]
	public void EagerNullFactoryDisposesContainerAndConsumesBuilder()
	{
		IResolver? captured = null;
		var builder = new ContainerBuilder();
		builder.Register<Resource>(resolver =>
		{
			captured = resolver;
			return null!;
		}, Lifetime.Singleton).NonLazy();

		var error = Assert.Throws<ResolutionException>(() => builder.Build());

		Assert.Contains("Factory returned null", error.Message);
		Assert.Throws<ObjectDisposedException>(() => captured!.Resolve<IResolver>());
		Assert.Throws<InvalidOperationException>(() => builder.Build());
	}

	[Fact]
	public void EagerFactoryCannotMutateOrRebuildTheConsumedBuilder()
	{
		var builder = new ContainerBuilder();
		RegistrationBuilder registration = null!;
		registration = builder.Register<Resource>(_ =>
		{
			Assert.Throws<InvalidOperationException>(() => builder.Register<Failure>(Lifetime.Singleton));
			Assert.Throws<InvalidOperationException>(() => builder.Build());
			Assert.Throws<InvalidOperationException>(() => registration.AsSelf());
			return new Resource();
		}, Lifetime.Singleton).NonLazy();

		using var container = builder.Build();
		Assert.NotNull(container.Resolve<Resource>());
	}

	[Fact]
	public void StaticCycleFailsValidationBeforeAnyEagerFactoryRuns()
	{
		var calls = 0;
		var builder = new ContainerBuilder();
		builder.Register<Resource>(_ =>
		{
			calls++;
			return new Resource();
		}, Lifetime.Singleton).NonLazy();
		builder.Register<CycleA>(Lifetime.Singleton);
		builder.Register<CycleB>(Lifetime.Singleton);

		Assert.Throws<RegistrationException>(() => builder.Build());
		Assert.Equal(0, calls);
	}

	public sealed class Resource(Action? cleanup = null) : IDisposable
	{
		public int Disposals { get; private set; }

		public void Dispose()
		{
			Disposals++;
			cleanup?.Invoke();
		}
	}
	public sealed class Failure;
	public sealed class CycleA(CycleB dependency)
	{
		public CycleB Dependency { get; } = dependency;
	}
	public sealed class CycleB(CycleA dependency)
	{
		public CycleA Dependency { get; } = dependency;
	}
}
