using Xunit;

namespace Injectra.Core.Tests.Resolution;

public sealed class InstanceOwnershipTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void InstanceOwnershipIsExplicitAndDoesNotRequireResolve(bool owned)
	{
		var resource = new Resource();
		var builder = new ContainerBuilder();
		var registration = builder.RegisterInstance(resource);
		if (owned)
			registration.OwnedByContainer();
		var container = builder.Build();
		container.Dispose();
		container.Dispose();
		Assert.Equal(owned ? 1 : 0, resource.DisposeCalls);
	}

	[Fact]
	public void FluentContractsPreserveOwnershipAndAliasesDisposeOnce()
	{
		var resource = new Resource();
		var builder = new ContainerBuilder();
		var registration = builder.RegisterInstance(resource).As<IDisposable>().OwnedByContainer().AsSelf();
		registration.OwnedByContainer();
		var container = builder.Build();
		Assert.Same(resource, container.Resolve<IDisposable>());
		Assert.Same(resource, container.Resolve<Resource>());
		Assert.Throws<InvalidOperationException>(() => registration.OwnedByContainer());
		container.Dispose();
		Assert.Equal(1, resource.DisposeCalls);
	}

	[Fact]
	public void FailedBuildDoesNotTakeOwnership()
	{
		var resource = new Resource();
		var builder = new ContainerBuilder();
		builder.RegisterInstance(resource).OwnedByContainer();
		builder.Register<MissingDependency>(Lifetime.Singleton);
		Assert.Throws<RegistrationException>(() => builder.Build());
		Assert.Equal(0, resource.DisposeCalls);
		builder.RegisterInstance(new object());
		builder.Build().Dispose();
		Assert.Equal(1, resource.DisposeCalls);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ExplicitOwnershipWinsForTheSameInstanceWithinOneContainer(bool ownedFirst)
	{
		var resource = new Resource();
		var builder = new ContainerBuilder();
		if (ownedFirst)
		{
			builder.RegisterInstance(resource).OwnedByContainer();
			builder.RegisterInstance<IDisposable>(resource);
		}
		else
		{
			builder.RegisterInstance<IDisposable>(resource);
			builder.RegisterInstance(resource).OwnedByContainer();
		}
		builder.Build().Dispose();
		Assert.Equal(1, resource.DisposeCalls);
	}

	[Fact]
	public void IndependentRegistrationsOfOwnedInstanceDisposeOnce()
	{
		var resource = new Resource();
		var builder = new ContainerBuilder();
		builder.RegisterInstance(resource).OwnedByContainer();
		builder.RegisterInstance<IDisposable>(resource).OwnedByContainer();
		builder.Build().Dispose();
		Assert.Equal(1, resource.DisposeCalls);
	}

	[Fact]
	public void ChildCannotTakeAnAncestorInstanceButCanBorrowIt()
	{
		var resource = new Resource();
		var builder = new ContainerBuilder();
		builder.RegisterInstance(resource).OwnedByContainer();
		var root = builder.Build();
		Assert.Throws<RegistrationException>(() => root.CreateChild(b => b.RegisterInstance(resource).OwnedByContainer()));
		using (var child = root.CreateChild(b => b.RegisterInstance<IDisposable>(resource)))
			Assert.Same(resource, child.Resolve<IDisposable>());
		Assert.Equal(0, resource.DisposeCalls);
		root.Dispose();
		Assert.Equal(1, resource.DisposeCalls);
	}

	[Fact]
	public void OwnedInstanceDisposalErrorsDoNotStopOtherCleanup()
	{
		var first = new Resource();
		var failing = new FailingResource();
		var builder = new ContainerBuilder();
		builder.RegisterInstance(first).OwnedByContainer();
		builder.RegisterInstance(failing).OwnedByContainer();
		var container = builder.Build();
		Assert.Throws<AggregateException>(() => container.Dispose());
		Assert.Equal(1, first.DisposeCalls);
		container.Dispose();
	}

	[Theory]
	[InlineData(Lifetime.Singleton)]
	[InlineData(Lifetime.Transient)]
	public void ConstructorRegistrationRejectsOwnershipOverride(Lifetime lifetime)
	{
		var builder = new ContainerBuilder();
		var registration = builder.Register<Resource>(lifetime);
		Assert.Throws<InvalidOperationException>(() => registration.OwnedByContainer());
		using var container = builder.Build();
		Assert.NotNull(container.Resolve<Resource>());
	}

	[Theory]
	[InlineData(Lifetime.Singleton)]
	[InlineData(Lifetime.Transient)]
	public void FactoryRegistrationRejectsOwnershipOverrideWithoutCallingFactory(Lifetime lifetime)
	{
		var calls = 0;
		var builder = new ContainerBuilder();
		var registration = builder.Register<Resource>(_ =>
		{
			calls++;
			return new Resource();
		}, lifetime);
		Assert.Throws<InvalidOperationException>(() => registration.OwnedByContainer());
		Assert.Equal(0, calls);
		using var container = builder.Build();
		Assert.NotNull(container.Resolve<Resource>());
	}

	[Fact]
	public void LaterOwnershipFailureDoesNotDisposeEarlierInstancesOrAttachChild()
	{
		var ancestorResource = new Resource();
		var localResource = new Resource();
		var builder = new ContainerBuilder();
		builder.RegisterInstance(ancestorResource);
		var root = builder.Build();
		Assert.Throws<RegistrationException>(() => root.CreateChild(child =>
		{
			child.RegisterInstance(localResource).OwnedByContainer();
			child.RegisterInstance<IDisposable>(ancestorResource).OwnedByContainer();
		}));
		root.Dispose();
		Assert.Equal(0, localResource.DisposeCalls);
		Assert.Equal(0, ancestorResource.DisposeCalls);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void FactoryAliasOfExplicitlyOwnedInstanceDisposesOnlyOnce(bool ownedFirst)
	{
		var resource = new Resource();
		var builder = new ContainerBuilder();
		if (ownedFirst)
			builder.RegisterInstance(resource).OwnedByContainer();
		builder.Register<IDisposable>(r => r.Resolve<Resource>(), Lifetime.Singleton);
		if (!ownedFirst)
			builder.RegisterInstance(resource).OwnedByContainer();
		var container = builder.Build();
		Assert.Same(resource, container.Resolve<IDisposable>());
		container.Dispose();
		Assert.Equal(1, resource.DisposeCalls);
	}

	public sealed class Resource : IDisposable
	{
		public int DisposeCalls { get; private set; }
		public void Dispose() => DisposeCalls++;
	}
	public sealed class MissingDependency(object dependency)
	{
		public object Dependency { get; } = dependency;
	}
	public sealed class FailingResource : IDisposable
	{
		public void Dispose() => throw new ApplicationException("Cleanup failed.");
	}
}
