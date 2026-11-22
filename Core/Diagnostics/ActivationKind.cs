namespace Injectra.Diagnostics;

/// <summary>The source used to obtain a registered service.</summary>
public enum ActivationKind
{
	/// <summary>A constructor, including source-generated constructor calls.</summary>
	Constructor,
	/// <summary>A user-supplied factory whose dependencies are opaque.</summary>
	Factory,
	/// <summary>An existing object supplied during registration.</summary>
	Instance
}
