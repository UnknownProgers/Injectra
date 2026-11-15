using Xunit;

namespace Injectra.Core.Tests.Resolution;

public sealed class ReentrancyTests
{
	[Fact]
	public void AliasCycleIsDetectedAndFailedResolutionCanBeRetried()
	{
		var recurse = true;
		var builder = new ContainerBuilder();
		builder.Register<Service>(resolver =>
		{
			if (recurse)
				resolver.Resolve<IService>();
			return new Service();
		}, Lifetime.Transient).As<IService>().AsSelf();
		using var container = builder.Build();

		var error = Assert.Throws<ResolutionException>(() => container.Resolve<Service>());
		Assert.Contains("Service -> Service", error.Message);
		recurse = false;
		Assert.IsType<Service>(container.Resolve<IService>());
		Assert.IsType<Service>(container.Resolve<Service>());
	}

	[Fact]
	public void ChildTransientCanResolveSameRegistrationInParent()
	{
		Container? parent = null;
		var builder = new ContainerBuilder();
		builder.Register<Service>(resolver =>
		{
			if (!ReferenceEquals(resolver, parent))
				parent!.Resolve<Service>();
			return new Service();
		}, Lifetime.Transient);
		using var root = builder.Build();
		parent = root;
		using var child = root.CreateChild(_ => { });

		Assert.IsType<Service>(child.Resolve<Service>());
	}

	public interface IService;
	public sealed class Service : IService;
}
