namespace Injectra.Diagnostics;

/// <summary>Who is responsible for a registration's instances.</summary>
public enum InstanceOwnership
{
	/// <summary>A container owns or will acquire the singleton.</summary>
	Container,
	/// <summary>An external caller owns the existing instance.</summary>
	External,
	/// <summary>The consumer owns each transient instance.</summary>
	Consumer
}
