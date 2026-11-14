using Injectra.Internal.Resolution;

namespace Injectra.Internal.Activation;

internal interface IActivation
{
	IReadOnlyList<Type> DependencyTypes { get; }
	IActivation WithDependencies(Binding[] dependencies);
	object Create(Container container);
}
