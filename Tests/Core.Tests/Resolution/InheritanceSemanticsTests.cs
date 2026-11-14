using Xunit;

namespace Injectra.Core.Tests.Resolution;

public sealed class InheritanceSemanticsTests
{
	[Fact]
	public void GrandchildrenRebindTransientsWithoutChangingParentGraphs()
	{
		var builder = new ContainerBuilder();
		var original = new Input(0);
		builder.RegisterInstance(original).As<IInput>();
		builder.Register<Controller>(Lifetime.Transient).As<IController>().AsSelf();
		using var root = builder.Build();
		using var first = root.CreateChild(b => b.RegisterInstance<IInput>(new Input(1)));
		using var second = root.CreateChild(b => b.RegisterInstance<IInput>(new Input(2)));
		using var grandchild = first.CreateChild(b => b.RegisterInstance<IInput>(new Input(3)));
		Assert.Equal(0, root.Resolve<Controller>().Input.Id);
		Assert.Equal(1, first.Resolve<Controller>().Input.Id);
		Assert.Equal(2, ((Controller)second.Resolve<IController>()).Input.Id);
		Assert.Equal(3, ((Controller)grandchild.Resolve<IController>()).Input.Id);
		Assert.Same(grandchild, grandchild.Resolve<Controller>().Resolver);
	}

	[Fact]
	public void ChildSingletonDependencyUsesItsOwnerInsteadOfGrandchildOverrides()
	{
		var builder = new ContainerBuilder();
		builder.RegisterInstance<IInput>(new Input(0));
		builder.Register<Controller>(Lifetime.Transient);
		using var root = builder.Build();
		using var child = root.CreateChild(b =>
		{
			b.RegisterInstance<IInput>(new Input(1));
			b.Register<Owner>(Lifetime.Singleton);
		});
		using var grandchild = child.CreateChild(b => b.RegisterInstance<IInput>(new Input(2)));
		var owner = grandchild.Resolve<Owner>();
		Assert.Equal(1, owner.Controller.Input.Id);
		Assert.Same(child, owner.Controller.Resolver);
		Assert.Equal(2, grandchild.Resolve<Controller>().Input.Id);
	}

	[Fact]
	public void InheritedFactoryCycleCanBeRetriedWithoutPoisoningOtherServices()
	{
		var builder = new ContainerBuilder();
		builder.RegisterInstance<IInput>(new Input(0));
		builder.Register<Controller>(r => (Controller)r.Resolve<IController>(), Lifetime.Transient)
			.As<IController>().AsSelf();
		using var root = builder.Build();
		using var child = root.CreateChild();
		for (var i = 0; i < 2; i++)
			Assert.Throws<ResolutionException>(() => child.Resolve<Controller>());
		Assert.Equal(0, child.Resolve<IInput>().Id);
	}

	[Fact]
	public void ChildCreationDoesNotExecuteInheritedConstructorsOrFactories()
	{
		var calls = 0;
		var builder = new ContainerBuilder();
		builder.Register<IInput>(_ =>
		{
			calls++;
			return new Input(0);
		}, Lifetime.Singleton);
		builder.Register<Controller>(Lifetime.Transient);
		using var root = builder.Build();
		using var child = root.CreateChild();
		Assert.Equal(0, calls);
		Assert.NotNull(child.Resolve<Controller>());
		Assert.Equal(1, calls);
	}

	public interface IInput
	{
		int Id { get; }
	}
	public sealed record Input(int Id) : IInput;
	public interface IController;
	public sealed class Controller(IInput input, IResolver resolver) : IController
	{
		public IInput Input { get; } = input;
		public IResolver Resolver { get; } = resolver;
	}
	public sealed class Owner(Controller controller)
	{
		public Controller Controller { get; } = controller;
	}
}
