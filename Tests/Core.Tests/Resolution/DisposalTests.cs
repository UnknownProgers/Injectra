using System.Runtime.CompilerServices;
using Xunit;

namespace Injectra.Core.Tests.Resolution;

public sealed class DisposalTests
{
	[Fact]
	public void SingletonAliasesDisposeOnceAndInstancesAreExternalByDefault()
	{
		var external = new Resource();
		var builder = new ContainerBuilder();
		builder.Register<Resource>(Lifetime.Singleton).As<IDisposable>().AsSelf();
		builder.RegisterInstance<object>(external);
		var container = builder.Build();
		var singleton = container.Resolve<Resource>();
		Assert.Same(singleton, container.Resolve<IDisposable>());
		container.Dispose();
		container.Dispose();
		Assert.Equal(1, singleton.DisposeCalls);
		Assert.Equal(0, external.DisposeCalls);
	}

	[Fact]
	public void TransientsAreDisposedByTheCaller()
	{
		var builder = new ContainerBuilder();
		builder.Register<Resource>(Lifetime.Transient);
		var container = builder.Build();
		var first = container.Resolve<Resource>();
		var second = container.Resolve<Resource>();
		first.Dispose();
		container.Dispose();
		Assert.Equal(1, first.DisposeCalls);
		Assert.Equal(0, second.DisposeCalls);
		second.Dispose();
	}

	[Fact]
	public void ParentClosesChildrenBeforeItsOwnServices()
	{
		var log = new List<string>();
		var builder = new ContainerBuilder();
		builder.Register<Resource>(_ => new Resource(() => log.Add("root")), Lifetime.Singleton);
		var root = builder.Build();
		root.Resolve<Resource>();
		var child = root.CreateChild(b => b.Register<Resource>(_ => new Resource(() => log.Add("child")), Lifetime.Singleton));
		child.Resolve<Resource>();
		root.Dispose();
		Assert.Equal(["child", "root"], log);
		Assert.Throws<ObjectDisposedException>(() => child.Resolve<Resource>());
	}

	[Fact]
	public void ChildDisposalDoesNotDisposeInheritedSingleton()
	{
		var builder = new ContainerBuilder();
		builder.Register<Resource>(Lifetime.Singleton);
		using var root = builder.Build();
		var child = root.CreateChild();
		var resource = child.Resolve<Resource>();
		child.Dispose();
		Assert.Equal(0, resource.DisposeCalls);
		Assert.Same(resource, root.Resolve<Resource>());
	}

	[Fact]
	public void DependenciesAreDisposedAfterConsumers()
	{
		var log = new List<string>();
		var builder = new ContainerBuilder();
		builder.RegisterInstance(log);
		builder.Register<Resource>(_ => new Resource(() => log.Add("dependency")), Lifetime.Singleton);
		builder.Register<Consumer>(Lifetime.Singleton);
		var container = builder.Build();
		container.Resolve<Consumer>();
		container.Dispose();
		Assert.Equal(["consumer", "dependency"], log);
	}

	[Fact]
	public void CleanupContinuesAfterErrorsAndStillClosesContainer()
	{
		var builder = new ContainerBuilder();
		var first = new Resource();
		var second = new Resource(() => throw new ApplicationException());
		builder.Register<IDisposable>(_ => first, Lifetime.Singleton);
		builder.Register<Resource>(_ => second, Lifetime.Singleton);
		var container = builder.Build();
		container.Resolve<IDisposable>();
		container.Resolve<Resource>();
		var error = Assert.Throws<AggregateException>(() => container.Dispose());
		Assert.Single(error.InnerExceptions);
		Assert.Equal(1, first.DisposeCalls);
		Assert.Equal(1, second.DisposeCalls);
		container.Dispose();
		Assert.Throws<ObjectDisposedException>(() => container.Resolve<IResolver>());
		Assert.Throws<ObjectDisposedException>(() => container.CreateChild());
	}

	[Fact]
	public void FactoryAliasesOfParentInstancesDoNotClaimOwnership()
	{
		var resource = new Resource();
		var builder = new ContainerBuilder();
		builder.Register<Resource>(_ => resource, Lifetime.Singleton);
		var root = builder.Build();
		var child = root.CreateChild(b => b.Register<IDisposable>(r => r.Resolve<Resource>(), Lifetime.Singleton));
		child.Resolve<IDisposable>();
		child.Dispose();
		Assert.Equal(0, resource.DisposeCalls);
		root.Dispose();
		Assert.Equal(1, resource.DisposeCalls);
	}

	[Fact]
	public void FactoryAliasOfExternalInstanceRemainsExternal()
	{
		var resource = new Resource();
		var builder = new ContainerBuilder();
		builder.RegisterInstance(resource);
		builder.Register<IDisposable>(r => r.Resolve<Resource>(), Lifetime.Singleton);
		var container = builder.Build();
		container.Resolve<IDisposable>();
		container.Dispose();
		Assert.Equal(0, resource.DisposeCalls);
	}

	[Fact]
	public void DisposalDuringActivationIsRejectedWithoutBreakingTheContainer()
	{
		var builder = new ContainerBuilder();
		builder.Register<Resource>(r =>
		{
			((Container)r).Dispose();
			return new Resource();
		}, Lifetime.Singleton);
		var container = builder.Build();
		Assert.Throws<InvalidOperationException>(() => container.Resolve<Resource>());
		Assert.Same(container, container.Resolve<IResolver>());
		container.Dispose();
	}

	[Fact]
	public void SiblingCleanupContinuesAfterChildThrows()
	{
		var log = new List<string>();
		var root = new ContainerBuilder().Build();
		root.CreateChild(b => b.Register<Resource>(_ => new Resource(() => log.Add("first")), Lifetime.Singleton)).Resolve<Resource>();
		root.CreateChild(b => b.Register<Resource>(_ => new Resource(() => throw new ApplicationException()), Lifetime.Singleton)).Resolve<Resource>();
		Assert.Throws<AggregateException>(() => root.Dispose());
		Assert.Equal(["first"], log);
		root.Dispose();
	}

	[Fact]
	public void DisposingASiblingDuringCleanupDoesNotSkipOtherChildren()
	{
		var root = new ContainerBuilder().Build();
		var firstResource = new Resource();
		var first = root.CreateChild(b => b.Register<Resource>(_ => firstResource, Lifetime.Singleton));
		first.Resolve<Resource>();
		root.CreateChild(b => b.Register<Resource>(_ => new Resource(first.Dispose), Lifetime.Singleton)).Resolve<Resource>();
		root.Dispose();
		Assert.Equal(1, firstResource.DisposeCalls);
	}

	[Fact]
	public void ResolveDuringCleanupIsRejectedButOtherCleanupContinues()
	{
		var builder = new ContainerBuilder();
		Container? container = null;
		builder.Register<Resource>(_ => new Resource(() => container!.Resolve<IResolver>()), Lifetime.Singleton);
		container = builder.Build();
		container.Resolve<Resource>();
		var exception = Assert.Throws<AggregateException>(() => container.Dispose());
		Assert.IsType<ObjectDisposedException>(Assert.Single(exception.InnerExceptions));
	}

	[Fact]
	public void FactorySingletonIsDisposedButFactoryTransientIsNot()
	{
		var singleton = new Resource();
		var transient = new Resource();
		var builder = new ContainerBuilder();
		builder.Register<Resource>(_ => singleton, Lifetime.Singleton);
		builder.Register<IDisposable>(_ => transient, Lifetime.Transient);
		var container = builder.Build();
		container.Resolve<Resource>();
		container.Resolve<IDisposable>();
		container.Dispose();
		Assert.Equal(1, singleton.DisposeCalls);
		Assert.Equal(0, transient.DisposeCalls);
	}

	[Fact]
	public void ContainerDoesNotRetainCreatedDisposableTransients()
	{
		var builder = new ContainerBuilder();
		builder.Register<Resource>(Lifetime.Transient);
		using var container = builder.Build();
		var reference = ResolveWeakReference(container);
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
		Assert.False(reference.IsAlive);
		GC.KeepAlive(container);
	}

	[Fact]
	public void CachedAncestorSingletonCannotBeResolvedWhileAncestorIsDisposing()
	{
		var builder = new ContainerBuilder();
		builder.Register<Resource>(Lifetime.Singleton);
		using var root = builder.Build();
		var waitingChild = root.CreateChild();
		waitingChild.Resolve<Resource>();
		root.CreateChild(child => child.RegisterInstance(new Resource(() =>
		{
			Assert.Throws<ObjectDisposedException>(() => waitingChild.Resolve<Resource>());
			Assert.Throws<ObjectDisposedException>(() => waitingChild.Resolve(typeof(Resource)));
		})).OwnedByContainer());

		root.Dispose();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference ResolveWeakReference(Container container) => new(container.Resolve<Resource>());

	public sealed class Resource : IDisposable
	{
		private readonly Action? _onDispose;
		public int DisposeCalls { get; private set; }

		public Resource()
		{
		}

		internal Resource(Action onDispose) => _onDispose = onDispose;

		public void Dispose()
		{
			DisposeCalls++;
			_onDispose?.Invoke();
		}
	}
	public sealed class Consumer(Resource resource, List<string> log) : IDisposable
	{
		public Resource Resource { get; } = resource;
		public void Dispose() => log.Add("consumer");
	}
}
