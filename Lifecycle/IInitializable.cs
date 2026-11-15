namespace Injectra.Lifecycle;

/// <summary>Initializes a participant before lifecycle dispatch begins.</summary>
public interface IInitializable
{
	/// <summary>Performs one-time initialization.</summary>
	void Initialize();
}
