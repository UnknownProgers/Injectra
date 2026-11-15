using Injectra.Internal.Activation;

namespace Injectra.Internal.Registration;

internal sealed record Registration(
	Type ImplementationType,
	Lifetime Lifetime,
	Type[] Contracts,
	IActivation? Activation,
	Func<IResolver, object>? Factory,
	object? Instance,
	bool OwnsInstance = false);
