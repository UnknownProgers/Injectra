using System.Reflection;

namespace Injectra.Internal.Registration;

internal sealed record Registration(
	Type ImplementationType,
	Lifetime Lifetime,
	Type[] Contracts,
	ConstructorInfo? Constructor,
	Func<IResolver, object>? Factory,
	object? Instance);
