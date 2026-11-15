using Xunit;

namespace Injectra.Core.Tests.Resolution;

public sealed class ResolveAllTests
{
	[Fact]
	public void EveryRegistrationKeepsItsLifetimeAndSingletonAliases()
	{
		var builder = new ContainerBuilder();
		builder.Register<Token>(Lifetime.Singleton);
		builder.Register<Service>(Lifetime.Singleton).As<IService>().AsSelf();
		builder.Register<Service>(Lifetime.Transient).As<IService>();
		using var container = builder.Build();
		var first = container.ResolveAll<IService>();
		var second = container.ResolveAll(typeof(IService));
		Assert.Equal(2, first.Count);
		Assert.Contains(container.Resolve<Service>(), first);
		Assert.Single(first, item => second.Any(other => ReferenceEquals(item, other)));
		Assert.Throws<ResolutionException>(() => container.Resolve<IService>());
	}

	[Fact]
	public void EmptyAndSingleResultsWorkAndDisposedOrOpenRequestsFail()
	{
		var builder = new ContainerBuilder();
		builder.Register<Token>(Lifetime.Singleton);
		builder.Register<Service>(Lifetime.Singleton).As<IService>();
		var container = builder.Build();
		Assert.Single(container.ResolveAll<IService>());
		Assert.Empty(container.ResolveAll<IDisposable>());
		Assert.Throws<ArgumentNullException>(() => container.ResolveAll(null!));
		Assert.Throws<ResolutionException>(() => container.ResolveAll(typeof(IGeneric<>)));
		container.Dispose();
		Assert.Throws<ObjectDisposedException>(() => container.ResolveAll<IService>());
	}

	[Fact]
	public void ChildOverridesTheWholeSetAndInheritedTransientsUseChildDependencies()
	{
		var rootToken = new Token();
		var childToken = new Token();
		var builder = new ContainerBuilder();
		builder.RegisterInstance(rootToken);
		builder.Register<Service>(Lifetime.Singleton).As<IService>();
		builder.Register<Service>(Lifetime.Transient).As<IService>();
		using var root = builder.Build();
		using var child = root.CreateChild(b => b.RegisterInstance(childToken));
		var inherited = child.ResolveAll<IService>();
		Assert.Single(inherited, item => ReferenceEquals(item.Token, rootToken));
		Assert.Single(inherited, item => ReferenceEquals(item.Token, childToken));
		using var local = root.CreateChild(b =>
			b.Register<IService>(_ => new Service(childToken), Lifetime.Singleton));
		Assert.Single(local.ResolveAll<IService>());
		Assert.Equal(2, root.ResolveAll<IService>().Count);
	}

	[Fact]
	public void EagerFactoriesAndInstancesDisposeOnlyAccordingToOwnership()
	{
		var owned = new Resource();
		var external = new Resource();
		var builder = new ContainerBuilder();
		builder.Register<IDisposable>(_ => owned, Lifetime.Singleton).NonLazy();
		builder.RegisterInstance<IDisposable>(external);
		var container = builder.Build();
		Assert.Equal(2, container.ResolveAll<IDisposable>().Count);
		container.Dispose();
		Assert.Equal(1, owned.Disposals);
		Assert.Equal(0, external.Disposals);
	}

	[Fact]
	public void AmbiguousConstructorDependenciesAndDuplicateConcreteTypesFailBuild()
	{
		var builder = new ContainerBuilder();
		builder.RegisterInstance(new Token());
		builder.Register<Service>(Lifetime.Singleton).As<IService>();
		builder.Register<Service>(Lifetime.Transient).As<IService>();
		builder.Register<Consumer>(Lifetime.Singleton);
		Assert.Throws<RegistrationException>(() => builder.Build());
		var duplicate = new ContainerBuilder();
		duplicate.Register<Token>(Lifetime.Singleton);
		duplicate.Register<Token>(Lifetime.Transient);
		Assert.Throws<RegistrationException>(() => duplicate.Build());
	}

	[Fact]
	public void MultipleOpenGenericImplementationsKeepClosedSingletonAliases()
	{
		var builder = new ContainerBuilder();
		builder.Register(typeof(Generic<>), Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
		builder.Register(typeof(Alternate<>), Lifetime.Transient).AsImplementedInterfaces();
		using var container = builder.Build();
		var all = container.ResolveAll<IGeneric<int>>();
		Assert.Equal(2, all.Count);
		Assert.Contains(container.Resolve<Generic<int>>(), all);
		Assert.Throws<ResolutionException>(() => container.Resolve<IGeneric<int>>());
	}

	[Fact]
	public void RecursiveFactoryCollectionResolutionFailsAndCanBeRetried()
	{
		var recurse = true;
		var builder = new ContainerBuilder();
		builder.Register<IService>(resolver =>
		{
			if (recurse) resolver.ResolveAll<IService>();
			return new Service(new Token());
		}, Lifetime.Singleton);
		using var container = builder.Build();
		Assert.Throws<ResolutionException>(() => container.ResolveAll<IService>());
		recurse = false;
		Assert.Single(container.ResolveAll<IService>());
	}

	public sealed class Token;
	public interface IService { Token Token { get; } }
	public sealed class Service(Token token) : IService
	{
		public Token Token { get; } = token;
	}
	public sealed class Consumer(IService service)
	{
		public IService Service { get; } = service;
	}
	public interface IGeneric<T>;
	public sealed class Generic<T> : IGeneric<T>;
	public sealed class Alternate<T> : IGeneric<T>;
	public sealed class Resource : IDisposable
	{
		public int Disposals;
		public void Dispose() => Disposals++;
	}
}
