namespace Injectra;

/// <summary>Specifies how a registration reuses the instances it creates.</summary>
public enum Lifetime
{
	/// <summary>Creates one instance shared by all contracts of the registration in its owning container.</summary>
	Singleton,
	/// <summary>Creates a new instance for each resolution.</summary>
	Transient
}
