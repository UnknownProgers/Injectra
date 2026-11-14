using Xunit;

namespace Injectra.Core.Tests.Resolution;

public sealed class ChildContainerTests
{
	[Fact]
	public void ChildrenInheritSingletonsButKeepLocalServicesPrivate()
	{
		var builder = new ContainerBuilder();
		builder.Register<Service>(Lifetime.Singleton);
		using var root = builder.Build();
		using var first = root.CreateChild(b => b.Register<Local>(Lifetime.Singleton));
		using var second = root.CreateChild();
		Assert.Same(root.Resolve<Service>(), first.Resolve<Service>());
		Assert.Throws<ResolutionException>(() => root.Resolve<Local>());
		Assert.Throws<ResolutionException>(() => second.Resolve<Local>());
		Assert.Same(first.Resolve<Local>(), first.Resolve<Local>());
	}

	[Fact]
	public void OverridesApplyOnlyToTheSelectedContract()
	{
		var original = new Service();
		var replacement = new Service();
		var builder = new ContainerBuilder();
		builder.RegisterInstance(original).As<IService>().AsSelf();
		using var root = builder.Build();
		using var child = root.CreateChild(b => b.RegisterInstance(replacement).As<IService>());
		Assert.Same(replacement, child.Resolve<IService>());
		Assert.Same(original, child.Resolve<Service>());
		Assert.Same(original, root.Resolve<IService>());
	}

	[Fact]
	public void InheritedSingletonUsesItsOwnerEvenWhenFirstRequestedByChild()
	{
		var original = new Service();
		var builder = new ContainerBuilder();
		builder.RegisterInstance(original).As<IService>();
		builder.Register<Consumer>(Lifetime.Singleton);
		using var root = builder.Build();
		using var child = root.CreateChild(b => b.RegisterInstance(new Service()).As<IService>());
		Assert.Same(original, child.Resolve<Consumer>().Service);
		Assert.Same(root.Resolve<Consumer>(), child.Resolve<Consumer>());
		Assert.Same(root, child.Resolve<Consumer>().Resolver);
	}

	[Fact]
	public void InheritedTransientUsesChildOverridesAndChildResolver()
	{
		var builder = new ContainerBuilder();
		builder.RegisterInstance(new Service()).As<IService>();
		builder.Register<Consumer>(Lifetime.Transient);
		using var root = builder.Build();
		var replacement = new Service();
		using var child = root.CreateChild(b => b.RegisterInstance(replacement).As<IService>());
		using var grandchild = child.CreateChild();
		Assert.Same(replacement, child.Resolve<Consumer>().Service);
		Assert.Same(child, child.Resolve<Consumer>().Resolver);
		Assert.Same(grandchild, grandchild.Resolve<Consumer>().Resolver);
		Assert.NotSame(child.Resolve<Consumer>(), child.Resolve<Consumer>());
		Assert.NotSame(replacement, root.Resolve<Consumer>().Service);
	}

	[Fact]
	public void InheritedFactoryTransientUsesChildContext()
	{
		var builder = new ContainerBuilder();
		builder.RegisterInstance(new Service()).As<IService>();
		builder.Register<Consumer>(r => new Consumer(r.Resolve<IService>(), r), Lifetime.Transient);
		using var root = builder.Build();
		var replacement = new Service();
		using var child = root.CreateChild(b => b.RegisterInstance(replacement).As<IService>());
		var consumer = child.Resolve<Consumer>();
		Assert.Same(replacement, consumer.Service);
		Assert.Same(child, consumer.Resolver);
	}

	[Fact]
	public void LocalSingletonsAreIndependentBetweenSiblings()
	{
		using var root = new ContainerBuilder().Build();
		using var first = root.CreateChild(b => b.Register<Local>(Lifetime.Singleton));
		using var second = root.CreateChild(b => b.Register<Local>(Lifetime.Singleton));
		Assert.NotSame(first.Resolve<Local>(), second.Resolve<Local>());
	}

	[Fact]
	public void ChildOverridesCanIntroduceACycleThroughInheritedTransients()
	{
		var builder = new ContainerBuilder();
		builder.RegisterInstance(new Service()).As<IService>();
		builder.Register<Consumer>(Lifetime.Transient);
		using var root = builder.Build();
		Assert.Throws<RegistrationException>(() => root.CreateChild(b =>
			b.Register<CyclicService>(Lifetime.Singleton).As<IService>()));
		Assert.NotNull(root.Resolve<Consumer>());
	}

	[Fact]
	public void FailedChildBuildDoesNotDisposeExternalInstances()
	{
		using var root = new ContainerBuilder().Build();
		var instance = new Service();
		Assert.Throws<RegistrationException>(() => root.CreateChild(b =>
		{
			b.RegisterInstance(instance);
			b.Register<Consumer>(Lifetime.Singleton);
		}));
		root.Dispose();
		Assert.Equal(0, instance.DisposeCalls);
	}

	[Fact]
	public void CallbackFailureLeavesParentUsable()
	{
		using var root = new ContainerBuilder().Build();
		Assert.Throws<ApplicationException>(() => root.CreateChild(_ => throw new ApplicationException()));
		using var child = root.CreateChild();
		Assert.Same(child, child.Resolve<IResolver>());
	}

	[Fact]
	public void CallbackCannotBuildAndAttachAnUnfinishedChild()
	{
		using var root = new ContainerBuilder().Build();
		Assert.Throws<InvalidOperationException>(() => root.CreateChild(b => b.Build()));
		using var child = root.CreateChild();
		Assert.Same(child, child.Resolve<IResolver>());
	}

	[Fact]
	public void CallbackCannotAttachAChildAfterDisposingTheParent()
	{
		var root = new ContainerBuilder().Build();
		Assert.Throws<ObjectDisposedException>(() => root.CreateChild(_ => root.Dispose()));
	}

	public interface IService;
	public sealed class Service : IService, IDisposable
	{
		public int DisposeCalls { get; private set; }
		public void Dispose() => DisposeCalls++;
	}
	public sealed class Local;
	public sealed class Consumer(IService service, IResolver resolver)
	{
		public IService Service { get; } = service;
		public IResolver Resolver { get; } = resolver;
	}
	public sealed class CyclicService(Consumer consumer) : IService
	{
		public Consumer Consumer { get; } = consumer;
	}
}
