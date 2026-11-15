using Injectra.Generation;
using Injectra.Internal.Resolution;

namespace Injectra.Internal.Activation;

internal sealed class GeneratedActivation(Type[] dependencyTypes, Func<ActivationContext, object> factory, Binding[] dependencies) : IActivation
{
	public IReadOnlyList<Type> DependencyTypes => dependencyTypes;
	public IActivation WithDependencies(Binding[] bindings) => new GeneratedActivation(dependencyTypes, factory, bindings);
	public object Create(Container container) => factory(new ActivationContext(container, dependencies));
}
