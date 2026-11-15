namespace Injectra.Lifecycle.Internal;

internal sealed class LifecycleDispatcher(Container container) : IDisposable
{
	private Container? _container = container;
	private Dictionary<RuntimeTypeHandle, IPhase>? _phases;
	internal bool IsStopped => _container is null;
	internal bool HasParticipants => _phases is not null;

	internal void AddPhase(RuntimeTypeHandle type, IPhase? phase)
	{
		if (phase is not null)
			(_phases ??= []).Add(type, phase);
	}

	internal void ValidateAgainst(LifecycleDispatcher other)
	{
		if (_phases is null || other._phases is null)
			return;

		foreach (var (type, phase) in _phases)
		{
			if (!other._phases.TryGetValue(type, out var existing))
				continue;

			var participants = phase.Participants;
			for (var i = 0; i < participants.Length; i++)
				if (existing.Contains(participants[i]))
					throw new RegistrationException($"The same participant is already attached for {Type.GetTypeFromHandle(type)}.");
		}
	}

	internal void Initialize() => Dispatch<IInitializable, byte>(0);

	internal void Dispatch<TPhase, TArgument>(TArgument argument) where TPhase : class
	{
		var source = _container;
		if (source is not null && _phases is not null && _phases.TryGetValue(typeof(TPhase).TypeHandle, out var phase))
			((Phase<TPhase, TArgument>)phase).Dispatch(source, argument);
	}

	public void Dispose()
	{
		if (_container is null)
			return;

		_container.ChildBuilt = null;
		_container.Closing = null;
		_container = null;
		_phases = null;
	}
}
