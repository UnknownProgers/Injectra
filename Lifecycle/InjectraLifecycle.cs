using Injectra.Lifecycle.Internal;

namespace Injectra.Lifecycle;

/// <summary>Initializes a fixed set of participants and dispatches explicitly registered lifecycle phases.</summary>
/// <remarks>
/// The application owns the loop and supplies timing. Participant order is unspecified.
/// Participants are not owned or disposed by this manager. Concurrent use is unsupported.
/// </remarks>
public sealed class InjectraLifecycle : IDisposable
{
	private object[] _participants;
	private IInitializable[] _initializables;
	private readonly Dictionary<RuntimeTypeHandle, object> _phases = [];
	private State _state;
	private bool _dispatching;

	/// <summary>Captures participants and registers the default frame phase.</summary>
	/// <param name="participants">Distinct objects participating in initialization or registered phases.</param>
	/// <exception cref="ArgumentNullException">The sequence is null.</exception>
	/// <exception cref="ArgumentException">The sequence contains a null participant or repeats the same instance.</exception>
	/// <remarks>Enumeration errors propagate. Construction does not initialize participants.</remarks>
	public InjectraLifecycle(IEnumerable<object> participants)
	{
		ArgumentNullException.ThrowIfNull(participants);
		var unique = new HashSet<object>(ReferenceEqualityComparer.Instance);
		foreach (var participant in participants)
		{
			if (participant is null)
				throw new ArgumentException("Participants cannot contain null.", nameof(participants));
			if (!unique.Add(participant))
				throw new ArgumentException("The same participant instance cannot be registered twice.", nameof(participants));
		}
		_participants = unique.ToArray();
		_initializables = _participants.OfType<IInitializable>().ToArray();
		RegisterPhase<IFrameTick, double>(static (participant, deltaTime) => participant.FrameTick(deltaTime));
	}

	/// <summary>Registers a custom phase and prepares its typed participant list before initialization.</summary>
	/// <typeparam name="TPhase">The interface identifying phase participants.</typeparam>
	/// <typeparam name="TArgument">The argument passed to each participant.</typeparam>
	/// <param name="invoke">The callback invoking the phase method.</param>
	/// <returns>This manager.</returns>
	/// <exception cref="ObjectDisposedException">The manager is disposed.</exception>
	/// <exception cref="InvalidOperationException">Initialization has started, or the phase is already registered.</exception>
	/// <exception cref="ArgumentNullException">The callback is null.</exception>
	/// <exception cref="ArgumentException">The phase type is not an interface, or is IInitializable.</exception>
	/// <remarks>Use a static callback to avoid capturing application state unnecessarily.</remarks>
	public InjectraLifecycle RegisterPhase<TPhase, TArgument>(Action<TPhase, TArgument> invoke) where TPhase : class
	{
		ThrowIfDisposed();
		if (_state != State.Configuring)
			throw new InvalidOperationException("Phases must be registered before initialization.");

		ArgumentNullException.ThrowIfNull(invoke);
		if (!typeof(TPhase).IsInterface || typeof(TPhase) == typeof(IInitializable))
			throw new ArgumentException("A phase must be an interface other than IInitializable.", nameof(TPhase));

		if (_phases.ContainsKey(typeof(TPhase).TypeHandle))
			throw new InvalidOperationException($"Phase {typeof(TPhase)} is already registered.");

		_phases.Add(typeof(TPhase).TypeHandle, new Phase<TPhase, TArgument>(_participants.OfType<TPhase>().ToArray(), invoke));
		return this;
	}

	/// <summary>Initializes all initializable participants once, before dispatch is allowed.</summary>
	/// <exception cref="ObjectDisposedException">The manager is disposed.</exception>
	/// <exception cref="InvalidOperationException">Initialization has already started, succeeded, or failed.</exception>
	/// <remarks>
	/// Participant exceptions propagate unchanged. Failure leaves dispatch disabled and cannot be retried;
	/// already performed initialization is not rolled back. Participant ownership remains with the caller.
	/// </remarks>
	public void Initialize()
	{
		ThrowIfDisposed();
		if (_state != State.Configuring)
			throw new InvalidOperationException("Initialization can only be attempted once.");
		_state = State.Initializing;
		try
		{
			foreach (var participant in _initializables)
				participant.Initialize();
			_state = State.Ready;
		}
		catch
		{
			_state = State.Faulted;
			throw;
		}
	}

	/// <summary>Dispatches the default IFrameTick phase.</summary>
	/// <param name="deltaTime">Elapsed frame time in seconds, supplied by the caller.</param>
	/// <exception cref="ObjectDisposedException">The manager is disposed.</exception>
	/// <exception cref="InvalidOperationException">Initialization has not succeeded, or a phase is already being dispatched.</exception>
	/// <remarks>Participant exceptions propagate unchanged and stop this dispatch. Later dispatches remain allowed.</remarks>
	public void Tick(double deltaTime) => Dispatch<IFrameTick, double>(deltaTime);

	/// <summary>Invokes a registered phase for all of its participants.</summary>
	/// <typeparam name="TPhase">The registered participant interface.</typeparam>
	/// <typeparam name="TArgument">The argument type used when the phase was registered.</typeparam>
	/// <param name="argument">The value passed to each participant.</param>
	/// <exception cref="ObjectDisposedException">The manager is disposed.</exception>
	/// <exception cref="InvalidOperationException">
	/// Initialization has not succeeded, dispatch is recursive, the phase is unregistered, or its argument type does not match.
	/// </exception>
	/// <remarks>Participant exceptions propagate unchanged and stop this dispatch. Participant order is unspecified.</remarks>
	public void Dispatch<TPhase, TArgument>(TArgument argument) where TPhase : class
	{
		ThrowIfDisposed();
		if (_state != State.Ready)
			throw new InvalidOperationException("Successful initialization is required before dispatch.");
		if (_dispatching)
			throw new InvalidOperationException("Recursive lifecycle dispatch is unsupported.");
		if (!_phases.TryGetValue(typeof(TPhase).TypeHandle, out var entry) || entry is not Phase<TPhase, TArgument> phase)
			throw new InvalidOperationException($"Phase {typeof(TPhase)} is not registered with argument type {typeof(TArgument)}.");
		_dispatching = true;
		try
		{
			phase.Dispatch(argument);
		}
		finally
		{
			_dispatching = false;
		}
	}

	/// <summary>Stops dispatch and releases references without disposing participants.</summary>
	/// <exception cref="InvalidOperationException">Initialization or dispatch is currently running.</exception>
	/// <remarks>Repeated calls have no effect.</remarks>
	public void Dispose()
	{
		if (_state == State.Disposed)
			return;
		if (_state == State.Initializing || _dispatching)
			throw new InvalidOperationException("Cannot dispose during a lifecycle callback.");
		_state = State.Disposed;
		_participants = [];
		_initializables = [];
		_phases.Clear();
	}

	private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_state == State.Disposed, this);

	private enum State { Configuring, Initializing, Ready, Faulted, Disposed }
}
