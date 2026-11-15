using Injectra.Lifecycle.Internal;

namespace Injectra.Lifecycle;

/// <summary>Initializes a fixed set of participants and dispatches explicitly registered lifecycle phases.</summary>
/// <remarks>
/// The application owns the loop and supplies timing. Participant order is unspecified.
/// Participants are not owned or disposed by this manager. Concurrent use is unsupported.
/// </remarks>
public sealed class InjectraLifecycle : IDisposable
{
	private IInitializable[] _initializables;
	private readonly Dictionary<RuntimeTypeHandle, object> _phases = [];
	private State _state;
	private bool _dispatching;
	private Container? _source;
	private bool _sealed;

	internal InjectraLifecycle(Container source)
	{
		if (!source.IsRoot)
			throw new InvalidOperationException("UseLifecycle currently supports root containers only.");

		_source = source;
		_initializables = ResolveParticipants<IInitializable>();

		RegisterPhase<IFrameTickable, double>(static (participant, deltaTime) => participant.FrameTick(deltaTime));
	}

	private T[] ResolveParticipants<T>() where T : class
	{
		_source!.EnsureSingletons(typeof(T));
		var participants = _source.ResolveAll<T>();
		var unique = new HashSet<object>(ReferenceEqualityComparer.Instance);

		foreach (var participant in participants)
		{
			if (!unique.Add(participant))
				throw new RegistrationException($"The same participant is registered more than once for {typeof(T)}.");
		}

		return participants.ToArray();
	}

	internal void SealConfiguration()
	{
		if (_state != State.Configuring)
			throw new InvalidOperationException("UseLifecycle configuration must not initialize or dispose the manager.");
		_sealed = true;
	}

	/// <summary>Registers a custom phase and prepares its typed participant list before initialization.</summary>
	/// <typeparam name="TPhase">The interface identifying phase participants.</typeparam>
	/// <typeparam name="TArgument">The argument passed to each participant.</typeparam>
	/// <param name="invoke">The callback invoking the phase method.</param>
	/// <returns>This manager.</returns>
	/// <exception cref="ObjectDisposedException">The manager or its owning container is disposed.</exception>
	/// <exception cref="InvalidOperationException">Initialization has started, DI configuration is complete, or the phase is already registered.</exception>
	/// <exception cref="ArgumentNullException">The callback is null.</exception>
	/// <exception cref="ArgumentException">The phase type is not an interface, or is IInitializable.</exception>
	/// <exception cref="RegistrationException">A container-backed phase contains a transient registration or repeats an instance.</exception>
	/// <remarks>Use a static callback to avoid capturing application state unnecessarily.</remarks>
	public InjectraLifecycle RegisterPhase<TPhase, TArgument>(Action<TPhase, TArgument> invoke) where TPhase : class
	{
		ThrowIfDisposed();
		if (_state != State.Configuring || _sealed)
			throw new InvalidOperationException("Phases must be registered before initialization or completion of UseLifecycle configuration.");

		ArgumentNullException.ThrowIfNull(invoke);
		if (!typeof(TPhase).IsInterface || typeof(TPhase) == typeof(IInitializable))
			throw new ArgumentException("A phase must be an interface other than IInitializable.", nameof(TPhase));

		if (_phases.ContainsKey(typeof(TPhase).TypeHandle))
			throw new InvalidOperationException($"Phase {typeof(TPhase)} is already registered.");

		var participants = ResolveParticipants<TPhase>();
		_phases.Add(typeof(TPhase).TypeHandle, new Phase<TPhase, TArgument>(participants, invoke));
		return this;
	}

	/// <summary>Initializes all initializable participants once, before dispatch is allowed.</summary>
	/// <exception cref="ObjectDisposedException">The manager or its owning container is disposed.</exception>
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
		_source?.BeginOperation();
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
		finally
		{
			_source?.EndOperation();
		}
	}

	/// <summary>Dispatches the default IFrameTickable phase.</summary>
	/// <param name="deltaTime">Elapsed frame time in seconds, supplied by the caller.</param>
	/// <exception cref="ObjectDisposedException">The manager or its owning container is disposed.</exception>
	/// <exception cref="InvalidOperationException">Initialization has not succeeded, or a phase is already being dispatched.</exception>
	/// <remarks>Participant exceptions propagate unchanged and stop this dispatch. Later dispatches remain allowed.</remarks>
	public void Tick(double deltaTime) => Dispatch<IFrameTickable, double>(deltaTime);

	/// <summary>Invokes a registered phase for all of its participants.</summary>
	/// <typeparam name="TPhase">The registered participant interface.</typeparam>
	/// <typeparam name="TArgument">The argument type used when the phase was registered.</typeparam>
	/// <param name="argument">The value passed to each participant.</param>
	/// <exception cref="ObjectDisposedException">The manager or its owning container is disposed.</exception>
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

		_source?.BeginOperation();
		_dispatching = true;
		try
		{
			phase.Dispatch(argument);
		}
		finally
		{
			_dispatching = false;
			_source?.EndOperation();
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
		_initializables = [];
		_phases.Clear();
		_source = null;
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_state == State.Disposed, this);
		_source?.ThrowIfDisposed();
	}

	private enum State { Configuring, Initializing, Ready, Faulted, Disposed }
}
