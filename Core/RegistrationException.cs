namespace Injectra;

/// <summary>Reports an invalid registration or constructor dependency graph.</summary>
/// <param name="message">The message describing the failure.</param>
public sealed class RegistrationException(string message) : ArgumentException(message);
