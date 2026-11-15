using System.Runtime.ExceptionServices;
using Injectra.Lifecycle.Internal;

namespace Injectra.Lifecycle;

/// <summary>Runs lifecycle phases through local dispatchers in a container hierarchy.</summary>
/// <remarks>
/// The application owns the loop and supplies timing. Participant order is unspecified.
/// Participants are not owned or disposed by this manager. Concurrent use is unsupported.
/// </remarks>
public sealed class InjectraLifecycle : IDisposable
{
	private Container? _source;
	private readonly LifecycleDispatcher _root;
	private readonly List<LifecycleDispatcher> _dispatchers = [];
	private readonly Dictionary<RuntimeTypeHandle, (Type Argument, Func<Container, IPhase?> Create)> _phases = [];
	private readonly Queue<Container> _pendingChildren = [];
	private State _state;
	private bool _sealed;
	private bool _running;

	internal InjectraLifecycle(Container source)
	{
		if (!source.IsRoot)
			throw new InvalidOperationException("UseLifecycle supports root builders only.");

		_source = source;
		_root = new LifecycleDispatcher(source);
		_dispatchers.Add(_root);
		AddPhase<IInitializable, byte>(static (participant, _) => participant.Initialize());
		AddPhase<IFrameTickable, double>(static (participant, deltaTime) => participant.FrameTick(deltaTime));
	}

	internal void SealConfiguration()
	{
		if (_state != State.Configuring)
			throw new InvalidOperationException("UseLifecycle configuration must not initialize or dispose the manager.");

		_sealed = true;
		_source!.Closing = _root.Dispose;
		_source!.ChildBuilt = ChildBuilt;
		foreach (var child in _source!.Children)
			_pendingChildren.Enqueue(child);

		ProcessChildren();
	}

	private void ChildBuilt(Container child)
	{
		if (_state is State.Faulted or State.Disposed)
			throw new InvalidOperationException("Cannot attach children to a stopped lifecycle.");

		_pendingChildren.Enqueue(child);
		if (!_running)
			ProcessChildren();
	}

	private void ProcessChildren()
	{
		_source!.BeginOperation();
		_running = true;
		try
		{
			DrainChildren();
		}
		finally
		{
			_running = false;
			_source!.EndOperation();
		}
	}

	private void AttachChild(Container child)
	{
		if (child.IsDisposed || child.Closing is not null)
			return;

		var dispatcher = new LifecycleDispatcher(child);
		try
		{
			foreach (var (type, phase) in _phases)
				dispatcher.AddPhase(type, phase.Create(child));

			if (dispatcher.HasParticipants)
			{
				foreach (var existing in _dispatchers)
					dispatcher.ValidateAgainst(existing);
			}

			child.Closing = dispatcher.Dispose;
			child.ChildBuilt = ChildBuilt;
			_dispatchers.Add(dispatcher);
			if (_state == State.Ready)
				dispatcher.Initialize();

			foreach (var descendant in child.Children)
				_pendingChildren.Enqueue(descendant);
		}
		catch
		{
			dispatcher.Dispose();
			throw;
		}
	}

	private void DrainChildren(Exception? callbackError = null)
	{
		List<Exception>? errors = null;
		while (_pendingChildren.TryDequeue(out var child))
		{
			try
			{
				if (_state == State.Faulted)
					child.Dispose();
				else
					AttachChild(child);
			}
			catch (Exception setupError)
			{
				(errors ??= []).Add(setupError);
				try
				{
					child.Dispose();
				}
				catch (Exception cleanupError)
				{
					errors.Add(cleanupError);
				}
			}
		}
		_dispatchers.RemoveAll(static dispatcher => dispatcher.IsStopped);

		if (errors is not null)
		{
			if (errors.Count == 1 && callbackError is null)
				ExceptionDispatchInfo.Capture(errors[0]).Throw();

			if (callbackError is not null)
				errors.Insert(0, callbackError);

			throw new AggregateException("Child lifecycle setup failed.", errors);
		}

		if (callbackError is not null)
			ExceptionDispatchInfo.Capture(callbackError).Throw();
	}

	private void AddPhase<TPhase, TArgument>(Action<TPhase, TArgument> invoke) where TPhase : class
	{
		var type = typeof(TPhase).TypeHandle;
		_root.AddPhase(type, Phase<TPhase, TArgument>.Create(_source!, invoke));
		_phases.Add(type, (typeof(TArgument), container => Phase<TPhase, TArgument>.Create(container, invoke)));
	}

	/// <summary>Registers a custom phase and prepares its local root participants.</summary>
	/// <typeparam name="TPhase">The interface identifying phase participants.</typeparam>
	/// <typeparam name="TArgument">The argument passed to each participant.</typeparam>
	/// <param name="invoke">The callback invoking the phase method.</param>
	/// <returns>This manager.</returns>
	/// <exception cref="ObjectDisposedException">The manager or its owning container is disposed.</exception>
	/// <exception cref="InvalidOperationException">Configuration is complete, initialization has started, or the phase is already registered.</exception>
	/// <exception cref="ArgumentNullException">The callback is null.</exception>
	/// <exception cref="ArgumentException">The phase type is not an interface, or is IInitializable.</exception>
	/// <exception cref="RegistrationException">A phase contains a transient registration or repeats an instance.</exception>
	/// <remarks>Use a static callback to avoid capturing application state unnecessarily.</remarks>
	public InjectraLifecycle RegisterPhase<TPhase, TArgument>(Action<TPhase, TArgument> invoke) where TPhase : class
	{
		ThrowIfDisposed();
		if (_state != State.Configuring || _sealed)
			throw new InvalidOperationException("Phases must be registered inside UseLifecycle configuration.");

		ArgumentNullException.ThrowIfNull(invoke);
		if (!typeof(TPhase).IsInterface || typeof(TPhase) == typeof(IInitializable))
			throw new ArgumentException("A phase must be an interface other than IInitializable.", nameof(TPhase));
		if (_phases.ContainsKey(typeof(TPhase).TypeHandle))
			throw new InvalidOperationException($"Phase {typeof(TPhase)} is already registered.");

		AddPhase(invoke);
		return this;
	}

	/// <summary>Initializes each container's local participants once, before dispatch is allowed.</summary>
	/// <exception cref="ObjectDisposedException">The manager or its owning container is disposed.</exception>
	/// <exception cref="InvalidOperationException">Initialization was already attempted or another lifecycle operation is running.</exception>
	/// <exception cref="AggregateException">Child setup or cleanup fails.</exception>
	/// <remarks>
	/// Initial participant exceptions leave dispatch disabled and cannot be retried. Pending children are then disposed.
	/// Children created during successful initialization join after traversal. Initialization already performed is not rolled back.
	/// Participant ownership remains with their containers or external owners.
	/// </remarks>
	public void Initialize()
	{
		ThrowIfDisposed();
		if (_state != State.Configuring || _running)
			throw new InvalidOperationException("Initialization can only be attempted once, outside another lifecycle operation.");

		_source!.BeginOperation();
		_running = true;
		_state = State.Initializing;
		Exception? callbackError = null;
		try
		{
			try
			{
				foreach (var dispatcher in _dispatchers)
					dispatcher.Initialize();

				_state = State.Ready;
			}
			catch (Exception error)
			{
				_state = State.Faulted;
				callbackError = error;
			}
			DrainChildren(callbackError);
		}
		finally
		{
			_running = false;
			_source!.EndOperation();
		}
	}

	/// <summary>Dispatches the default IFrameTickable phase.</summary>
	/// <param name="deltaTime">Elapsed frame time in seconds, supplied by the caller.</param>
	/// <exception cref="ObjectDisposedException">The manager or its owning container is disposed.</exception>
	/// <exception cref="InvalidOperationException">Initialization has not succeeded, or another lifecycle operation is running.</exception>
	/// <exception cref="AggregateException">Child setup or cleanup fails; any dispatch exception is included.</exception>
	/// <remarks>Participant exceptions stop this dispatch. Later dispatches remain allowed.</remarks>
	public void Tick(double deltaTime) => Dispatch<IFrameTickable, double>(deltaTime);

	/// <summary>Invokes a registered phase through every active local dispatcher.</summary>
	/// <typeparam name="TPhase">The registered participant interface.</typeparam>
	/// <typeparam name="TArgument">The argument type used when the phase was registered.</typeparam>
	/// <param name="argument">The value passed to each participant.</param>
	/// <exception cref="ObjectDisposedException">The manager or its owning container is disposed.</exception>
	/// <exception cref="InvalidOperationException">Initialization has not succeeded, another operation is running, or the phase or argument type is invalid.</exception>
	/// <exception cref="AggregateException">Child setup or cleanup fails; any dispatch exception is included.</exception>
	/// <remarks>
	/// New children join after traversal and participate with the next phase call. Child disposal is synchronous;
	/// subsequent callbacks in the disposed subtree are skipped. Participant order is unspecified.
	/// </remarks>
	public void Dispatch<TPhase, TArgument>(TArgument argument) where TPhase : class
	{
		ThrowIfDisposed();
		if (_state != State.Ready || _running)
			throw new InvalidOperationException("Successful initialization and a callback-free lifecycle boundary are required.");
		if (typeof(TPhase) == typeof(IInitializable) || !_phases.TryGetValue(typeof(TPhase).TypeHandle, out var phase) ||
		    phase.Argument != typeof(TArgument))
			throw new InvalidOperationException($"Phase {typeof(TPhase)} is not registered.");

		_source!.BeginOperation();
		_running = true;
		Exception? callbackError = null;
		try
		{
			try
			{
				foreach (var dispatcher in _dispatchers)
					dispatcher.Dispatch<TPhase, TArgument>(argument);
			}
			catch (Exception error)
			{
				callbackError = error;
			}
			DrainChildren(callbackError);
		}
		finally
		{
			_running = false;
			_source!.EndOperation();
		}
	}

	/// <summary>Stops local dispatchers and releases references without disposing participants.</summary>
	/// <exception cref="InvalidOperationException">Another lifecycle operation is currently running.</exception>
	/// <remarks>Repeated calls have no effect.</remarks>
	public void Dispose()
	{
		if (_state == State.Disposed)
			return;

		if (_running || _state == State.Initializing)
			throw new InvalidOperationException("Cannot dispose during a lifecycle callback.");

		_state = State.Disposed;
		_source!.ChildBuilt = null;
		foreach (var dispatcher in _dispatchers)
			dispatcher.Dispose();
		_dispatchers.Clear();
		_pendingChildren.Clear();
		_phases.Clear();
		_source = null;
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_state == State.Disposed, this);
		_source!.ThrowIfDisposed();
	}

	private enum State
	{
		Configuring,
		Initializing,
		Ready,
		Faulted,
		Disposed
	}
}
