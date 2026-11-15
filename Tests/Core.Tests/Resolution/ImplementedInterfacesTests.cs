using Xunit;

namespace Injectra.Core.Tests.Resolution;

public sealed class ImplementedInterfacesTests
{
	[Fact]
	public void InterfacesAndSelfShareSingletonAndDisposeOnce()
	{
		var builder = new ContainerBuilder();
		var registration = builder.Register<Service>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
		var container = builder.Build();
		var service = container.Resolve<Service>();
		Assert.Same(service, container.Resolve<IService>());
		Assert.Same(service, container.Resolve<IBase>());
		Assert.Same(service, container.Resolve<IDisposable>());
		container.Dispose();
		Assert.Equal(1, service.Disposals);
		Assert.Throws<InvalidOperationException>(() => registration.AsImplementedInterfaces());
	}

	[Fact]
	public void ExplicitInterfacesRemoveImplicitSelfAndKeepExternalOwnership()
	{
		var service = new Service();
		var builder = new ContainerBuilder();
		builder.RegisterInstance(service).AsImplementedInterfaces();
		using var container = builder.Build();
		Assert.Same(service, container.Resolve<IService>());
		Assert.Throws<ResolutionException>(() => container.Resolve<Service>());
		container.Dispose();
		Assert.Equal(0, service.Disposals);
	}

	[Fact]
	public void FailedDuplicateExpansionDoesNotPartiallyAddInterfaces()
	{
		var builder = new ContainerBuilder();
		var registration = builder.Register<Service>(Lifetime.Singleton).As<IService>();
		Assert.Throws<RegistrationException>(() => registration.AsImplementedInterfaces());
		using var container = builder.Build();
		Assert.Throws<ResolutionException>(() => container.Resolve<IBase>());
		Assert.Throws<ResolutionException>(() => container.Resolve<IDisposable>());
	}

	[Fact]
	public void OpenGenericInterfacesMapPositionallyAndShareSelfAlias()
	{
		var builder = new ContainerBuilder();
		builder.Register(typeof(Generic<>), Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
		using var container = builder.Build();
		Assert.Same(container.Resolve<Generic<int>>(), container.Resolve<IGeneric<int>>());
	}

	[Fact]
	public void UnsupportedOpenInterfacesFailAtomicallyAndNoInterfacesKeepSelf()
	{
		var builder = new ContainerBuilder();
		builder.Register<Plain>(Lifetime.Singleton).AsImplementedInterfaces();
		var registration = builder.Register(typeof(Invalid<>), Lifetime.Singleton);
		Assert.Throws<RegistrationException>(() => registration.AsImplementedInterfaces());
		using var container = builder.Build();
		Assert.NotNull(container.Resolve<Plain>());
		Assert.NotNull(container.Resolve<Invalid<int>>());
		Assert.Throws<ResolutionException>(() => container.Resolve<IGeneric<int>>());
	}

	public interface IBase;
	public interface IService : IBase;
	public sealed class Service : IService, IDisposable
	{
		public int Disposals { get; private set; }
		public void Dispose() => Disposals++;
	}
	public interface IGeneric<T>;
	public sealed class Generic<T> : IGeneric<T>;
	public sealed class Invalid<T> : IGeneric<T>, IDisposable
	{
		public void Dispose()
		{
		}
	}
	public sealed class Plain;
}
