namespace Injectra.Lifecycle.Internal;

internal sealed class Phase<TParticipant, TArgument>(TParticipant[] participants, Action<TParticipant, TArgument> invoke)
	where TParticipant : class
{
	internal void Dispatch(TArgument argument)
	{
		foreach (var participant in participants)
			invoke(participant, argument);
	}
}
