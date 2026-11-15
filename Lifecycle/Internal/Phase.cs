namespace Injectra.Lifecycle.Internal;

internal interface IPhase
{
	object[] Participants { get; }
	bool Contains(object participant);
}
internal sealed class Phase<TParticipant, TArgument> : IPhase where TParticipant : class
{
	private readonly TParticipant[] _participants;
	private readonly Action<TParticipant, TArgument> _invoke;
	private readonly HashSet<object> _known;

	internal static Phase<TParticipant, TArgument>? Create(Container container, Action<TParticipant, TArgument> invoke)
	{
		var participants = container.ResolveLocalSingletons<TParticipant>();
		return participants.Length == 0 ? null : new Phase<TParticipant, TArgument>(participants, invoke);
	}

	private Phase(TParticipant[] participants, Action<TParticipant, TArgument> invoke)
	{
		_participants = participants;
		_invoke = invoke;
		_known = new HashSet<object>(_participants, ReferenceEqualityComparer.Instance);
		if (_known.Count != _participants.Length)
			throw new RegistrationException($"The same participant is registered more than once for {typeof(TParticipant)}.");
	}

	public object[] Participants => _participants;
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
