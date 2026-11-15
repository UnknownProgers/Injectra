namespace Injectra.Lifecycle;

/// <summary>Receives frame updates from the application or engine adapter.</summary>
public interface IFrameTickable
{
	/// <summary>Updates the participant for one frame.</summary>
	/// <param name="deltaTime">Elapsed frame time in seconds, supplied by the caller.</param>
	void FrameTick(double deltaTime);
}
