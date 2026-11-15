namespace Injectra.Lifecycle.Internal;

internal interface IPhase
{
	IReadOnlyList<object> Participants { get; }
	bool Contains(object participant);
}
internal sealed class Phase<TParticipant, TArgument> : IPhase where TParticipant : class
{
	private readonly TParticipant[] _participants;
	private readonly Action<TParticipant, TArgument> _invoke;
	private readonly HashSet<object> _known;

	internal Phase(Container container, Action<TParticipant, TArgument> invoke)
	{
		_participants = container.ResolveLocalSingletons<TParticipant>();
		_invoke = invoke;
		_known = new HashSet<object>(_participants, ReferenceEqualityComparer.Instance);
		if (_known.Count != _participants.Length)
			throw new RegistrationException($"The same participant is registered more than once for {typeof(TParticipant)}.");
	}

	public IReadOnlyList<object> Participants => _participants;
	public bool Contains(object participant) => _known.Contains(participant);

	internal void Dispatch(Container container, TArgument argument)
	{
		foreach (var participant in _participants)
		{
			if (container.IsDisposed)
				break;
			_invoke(participant, argument);
		}
	}
}
