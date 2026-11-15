namespace Injectra.Lifecycle.Internal;

internal interface IPhase
{
	void Attach(Container container);
	void Detach(Container container);
}

internal sealed class Phase<TParticipant, TArgument>(TParticipant[] participants, Action<TParticipant, TArgument> invoke) : IPhase
	where TParticipant : class
{
	private readonly Participants<TParticipant> _participants = new(participants);

	public void Attach(Container container) => _participants.Attach(container);
	public void Detach(Container container) => _participants.Detach(container);

	internal void Dispatch(TArgument argument)
	{
		foreach (var participant in _participants.Root)
			invoke(participant, argument);

		foreach (var (container, members) in _participants.Children)
		{
			container.BeginOperation();
			try
			{
				foreach (var participant in members)
					invoke(participant, argument);
			}
			finally
			{
				container.EndOperation();
			}
		}
	}
}
