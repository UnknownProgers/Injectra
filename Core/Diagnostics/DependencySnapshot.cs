namespace Injectra.Diagnostics;

/// <summary>A constructor dependency described in its requesting container's context.</summary>
/// <param name="ContractType">Requested constructor parameter type.</param>
/// <param name="ImplementationType">Selected implementation, or null if missing or ambiguous.</param>
/// <param name="DeclaringContainerId">Source container, or null if no registration exists.</param>
/// <param name="Activation">Selected creation mechanism, or null if missing or ambiguous.</param>
/// <param name="IsReference">The same registration and resolution context were already expanded in this graph.</param>
/// <param name="IsAmbiguous">More than one registration exposes this contract.</param>
/// <param name="Dependencies">Nested constructor dependencies, expanded without preparing or resolving bindings.</param>
public sealed record DependencySnapshot(
	Type ContractType,
	Type? ImplementationType,
	int? DeclaringContainerId,
	ActivationKind? Activation,
	bool IsReference,
	bool IsAmbiguous,
	IReadOnlyList<DependencySnapshot> Dependencies);
