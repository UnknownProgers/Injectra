using Injectra.Internal.Resolution;

namespace Injectra.Internal.Activation;

internal sealed class FactoryActivation(Func<IResolver, object> factory) : IActivation
{
	private Func<IResolver, object> Factory { get; } = factory;
	public IReadOnlyList<Type> DependencyTypes => Array.Empty<Type>();
	public IActivation WithDependencies(Binding[] dependencies) => this;

	public object Create(Container container) => Factory(container)
		?? throw new ResolutionException($"Factory returned null: {container.FormatPath()}.");
}
