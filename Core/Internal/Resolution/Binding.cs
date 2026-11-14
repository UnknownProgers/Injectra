using Injectra.Internal.Activation;

namespace Injectra.Internal.Resolution;

internal sealed class Binding(Type implementationType, Lifetime lifetime, object? instance = null)
{
	internal bool Prepared { get; set; }
	internal Container Owner { get; set; } = null!;
	internal Type ImplementationType { get; } = implementationType;
	internal Lifetime Lifetime { get; } = lifetime;
	internal IActivation Activation { get; set; } = null!;
	internal object? Instance { get; set; } = instance;
}
