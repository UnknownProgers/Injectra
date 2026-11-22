namespace Injectra.Diagnostics;

/// <summary>A detached, point-in-time description of a container subtree. Does not retain service instances.</summary>
/// <param name="Id">Container identifier, unique within this capture's hierarchy.</param>
/// <param name="ParentId">Parent identifier, or null for a root container.</param>
/// <param name="Registrations">Local registrations and cached generic specializations, sorted created-first, then by type name.</param>
/// <param name="Children">Live child containers, in creation order.</param>
public sealed record ContainerSnapshot(
	int Id,
	int? ParentId,
	IReadOnlyList<RegistrationSnapshot> Registrations,
	IReadOnlyList<ContainerSnapshot> Children)
{
	/// <summary>Formats this snapshot as a text tree without accessing the live container.</summary>
	public override string ToString() => DiagnosticTree.Format(this);
}
