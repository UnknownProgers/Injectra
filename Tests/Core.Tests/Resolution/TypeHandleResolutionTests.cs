using System.Reflection;
using Xunit;

namespace Injectra.Core.Tests.Resolution;

public sealed class TypeHandleResolutionTests
{
	[Fact]
	public void WrappedRuntimeTypeResolvesTheSameRegistration()
	{
		var builder = new ContainerBuilder();
		builder.Register<Service>(Lifetime.Singleton).As<IService>().AsSelf();
		using var container = builder.Build();

		Assert.Same(container.Resolve<Service>(), container.Resolve(new TypeDelegator(typeof(IService))));
		Assert.Same(container, container.Resolve(new TypeDelegator(typeof(IResolver))));
	}

	[Fact]
	public void WrappedContractCannotBypassDuplicateRegistrationValidation()
	{
		var builder = new ContainerBuilder();
		builder.Register<Service>(Lifetime.Singleton).As<IService>();
		builder.Register<Service>(Lifetime.Singleton).As(new TypeDelegator(typeof(IService)));

		Assert.Throws<RegistrationException>(() => builder.Build());
	}

	[Fact]
	public void WrappedResolverContractIsStillReserved()
	{
		var builder = new ContainerBuilder();
		using var external = new ContainerBuilder().Build();
		builder.RegisterInstance(external).As(new TypeDelegator(typeof(IResolver)));

		Assert.Throws<RegistrationException>(() => builder.Build());
	}

	[Fact]
	public void ClosedContractsRemainDistinctAndRespectChildOverrides()
	{
		var builder = new ContainerBuilder();
		builder.Register(typeof(Generic<>), Lifetime.Singleton);
		using var root = builder.Build();
		var number = root.Resolve<Generic<int>>();
		var text = root.Resolve<Generic<string>>();
		using var child = root.CreateChild(b => b.Register<Generic<int>>(Lifetime.Singleton));

		Assert.NotSame(number, text);
		Assert.NotSame(number, child.Resolve<Generic<int>>());
		Assert.Same(text, child.Resolve<Generic<string>>());
		Assert.Same(text, child.Resolve(new TypeDelegator(typeof(Generic<string>))));
		Assert.Throws<ResolutionException>(() => child.Resolve(typeof(Generic<>)));
	}

	public interface IService;
	public sealed class Service : IService;
	public sealed class Generic<T>;
}
