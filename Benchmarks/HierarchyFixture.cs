namespace Injectra.Benchmarks;

internal sealed class HierarchyFixture : IDisposable
{
	internal Container Root { get; }
	internal Container Parent { get; }
	internal Container Leaf { get; }

	internal HierarchyFixture(int registrations, int depth)
	{
		var builder = new ContainerBuilder();
		LayoutMeasurements.RegisterFillers(builder, registrations);
		builder.RegisterInstance<LayoutMeasurements.IInput>(new LayoutMeasurements.Input());
		builder.Register<LayoutMeasurements.Dependency>(Lifetime.Transient);
		builder.Register<LayoutMeasurements.Consumer>(Lifetime.Transient);
		Root = builder.Build();
		Root.Resolve(LayoutMeasurements.Services[0]);
		Parent = Root;
		for (var i = 1; i < depth; i++) Parent = Parent.CreateChild();
		Leaf = CreateChild();
	}

	internal Container CreateChild() => Parent.CreateChild(builder =>
		builder.RegisterInstance<LayoutMeasurements.IInput>(new LayoutMeasurements.Input()));

	public void Dispose() => Root.Dispose();
}
