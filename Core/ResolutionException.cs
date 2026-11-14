namespace Injectra;

/// <summary>Reports a service resolution failure detected by the container.</summary>
/// <param name="message">The message describing the failure.</param>
public sealed class ResolutionException(string message) : InvalidOperationException(message);
