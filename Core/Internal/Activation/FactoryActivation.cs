namespace Injectra.Internal.Activation;

internal sealed class FactoryActivation(Func<IResolver, object> factory) : IActivation
{
	public object Create(Container container) => factory(container)
		?? throw new ResolutionException($"Factory returned null: {container.FormatPath()}.");
}
