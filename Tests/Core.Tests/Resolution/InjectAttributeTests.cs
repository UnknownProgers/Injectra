using System.Reflection;
using Xunit;

namespace Injectra.Core.Tests.Resolution;

public sealed class InjectAttributeTests
{
	[Fact]
	public void OverriddenMethodInheritsTheInjectionMarker()
	{
		var method = typeof(DerivedService).GetMethod(nameof(DerivedService.Construct))!;

		Assert.NotNull(method.GetCustomAttribute<InjectAttribute>(inherit: true));
		Assert.Null(method.GetCustomAttribute<InjectAttribute>(inherit: false));
	}

	[Fact]
	public void MarkedMethodsAreNotImplicitlyInvokedByTheContainer()
	{
		var external = new Service();
		var builder = new ContainerBuilder();
		builder.Register<Service>(Lifetime.Singleton).NonLazy();
		builder.RegisterInstance(external).As<IService>();
		using var container = builder.Build();

		Assert.False(container.Resolve<Service>().Injected);
		Assert.Same(external, container.Resolve<IService>());
		Assert.False(external.Injected);
	}

	private interface IService;

	private class BaseService
	{
		[Inject]
		public virtual void Construct()
		{
		}
	}

	private sealed class DerivedService : BaseService
	{
		public override void Construct()
		{
		}
	}

	private sealed class Service : IService
	{
		public bool Injected { get; private set; }

		[Inject]
		public void Construct() => Injected = true;
	}
}
