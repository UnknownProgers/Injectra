namespace Injectra.Diagnostics;

/// <summary>One registration with all its aliases, without references to services or activation delegates.</summary>
/// <param name="ImplementationType">The registered implementation type.</param>
/// <param name="Contracts">All contracts exposing this registration.</param>
/// <param name="Lifetime">The registration's lifetime.</param>
/// <param name="Activation">Constructor, factory, or instance registration.</param>
/// <param name="IsCreated">An instance is cached or was supplied. Always false for transients.</param>
/// <param name="Ownership">Responsibility for the service's lifetime.</param>
/// <param name="OwnerContainerId">Actual or expected owning container, or null for external and consumer ownership.</param>
/// <param name="DeclaringContainerId">The container declaring the registration, including a generic definition's origin.</param>
/// <param name="IsSpecialization">This entry describes an already cached closed generic, not a new registration.</param>
/// <param name="OverriddenContracts">Contracts hiding ancestor registrations.</param>
/// <param name="Dependencies">Constructor dependencies. Factories and unclosed generic definitions are not expanded.</param>
public sealed record RegistrationSnapshot(
	Type ImplementationType,
	IReadOnlyList<Type> Contracts,
	Lifetime Lifetime,
	ActivationKind Activation,
	bool IsCreated,
	InstanceOwnership Ownership,
	int? OwnerContainerId,
	int DeclaringContainerId,
	bool IsSpecialization,
	IReadOnlyList<Type> OverriddenContracts,
	IReadOnlyList<DependencySnapshot> Dependencies);
