namespace Injectra.Lifecycle.Internal;

internal sealed class LifecycleDispatcher(Container container) : IDisposable
{
	private Container? _container = container;
	private readonly Dictionary<RuntimeTypeHandle, IPhase> _phases = [];
	internal bool IsStopped => _container is null;

	internal void AddPhase(RuntimeTypeHandle type, IPhase phase) => _phases.Add(type, phase);

	internal void ValidateAgainst(LifecycleDispatcher other)
	{
		foreach (var (type, phase) in _phases)
		{
			if (!other._phases.TryGetValue(type, out var existing))
				continue;

			foreach (var participant in phase.Participants)
				if (existing.Contains(participant))
					throw new RegistrationException($"The same participant is already attached for {Type.GetTypeFromHandle(type)}.");
		}
	}

	internal void Initialize() => Dispatch<IInitializable, byte>(0);

	internal void Dispatch<TPhase, TArgument>(TArgument argument) where TPhase : class
	{
		var source = _container;
		if (source is not null)
			((Phase<TPhase, TArgument>)_phases[typeof(TPhase).TypeHandle]).Dispatch(source, argument);
	}

	internal void Stop(Container _) => Dispose();

	public void Dispose()
	{
		if (_container is null)
			return;

		_container.ChildBuilt = null;
		_container.Closing = null;
		_container = null;
		_phases.Clear();
	}
}
