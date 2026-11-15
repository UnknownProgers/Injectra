using Xunit;

namespace Injectra.Core.Tests.Resolution;

public sealed class ReentrancyTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void AliasCycleIsDetectedAndFailedResolutionCanBeRetried(bool runtimeType)
	{
		var recurse = true;
		var builder = new ContainerBuilder();
		builder.Register<Service>(resolver =>
		{
			if (recurse)
			{
				if (runtimeType)
					resolver.Resolve(typeof(IService));
				else
					resolver.Resolve<IService>();
			}
			return new Service();
		}, Lifetime.Transient).As<IService>().AsSelf();
		using var container = builder.Build();

		var error = Assert.Throws<ResolutionException>(() => runtimeType
			? container.Resolve(typeof(Service))
			: container.Resolve<Service>());
		Assert.Contains("Service -> Service", error.Message);
		recurse = false;
		Assert.IsType<Service>(runtimeType ? container.Resolve(typeof(IService)) : container.Resolve<IService>());
		Assert.IsType<Service>(runtimeType ? container.Resolve(typeof(Service)) : container.Resolve<Service>());
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
