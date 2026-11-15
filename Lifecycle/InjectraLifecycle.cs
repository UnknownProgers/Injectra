using System.Runtime.ExceptionServices;
using Injectra.Lifecycle.Internal;

namespace Injectra.Lifecycle;

/// <summary>Initializes participants from a container hierarchy and dispatches explicitly registered lifecycle phases.</summary>
/// <remarks>
/// The application owns the loop and supplies timing. Participant order is unspecified.
/// Participants are not owned or disposed by this manager. Concurrent use is unsupported.
/// </remarks>
public sealed class InjectraLifecycle : IDisposable
{
	private Participants<IInitializable> _initializables;
	private readonly Dictionary<RuntimeTypeHandle, IPhase> _phases = [];
	private State _state;
	private bool _dispatching;
	private Container? _source;
	private bool _sealed;
	private int _attachingChildren;
	private readonly Queue<Container> _pendingChildren = [];
	private bool _drainingChildren;

	internal InjectraLifecycle(Container source)
	{
		if (!source.IsRoot)
			throw new InvalidOperationException("UseLifecycle currently supports root containers only.");

		_source = source;
		_initializables = new Participants<IInitializable>(ResolveParticipants<IInitializable>());

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
		_source!.ChildBuilt = ChildBuilt;

		foreach (var child in _source.Children.ToArray())
			AttachChild(child);
	}

	private void ChildBuilt(Container child)
	{
		if (_state is State.Faulted or State.Disposed)
			throw new InvalidOperationException("Cannot attach children to a stopped lifecycle.");

		if (_dispatching || _state == State.Initializing || _attachingChildren != 0)
		{
			_pendingChildren.Enqueue(child);
			return;
		}

		AttachChild(child);
		DrainChildren();
	}

	private void AttachChild(Container child)
	{
		if (child.IsDisposed || _initializables.Children.ContainsKey(child))
			return;

		_attachingChildren++;
		child.ChildBuilt = ChildBuilt;
		try
		{
			_initializables.Attach(child);
			foreach (var phase in _phases.Values)
				phase.Attach(child);

			child.Closing = DetachChild;
			if (_state == State.Ready)
				InitializeChild(child, _initializables.Children[child]);
		}
		catch
		{
			_initializables.Detach(child);
			foreach (var phase in _phases.Values)
				phase.Detach(child);

			child.ChildBuilt = null;
			child.Closing = null;
			throw;
		}
		finally
		{
			_attachingChildren--;
		}

		foreach (var descendant in child.Children.ToArray())
			AttachChild(descendant);
	}

	private void DrainChildren(Exception? callbackError = null)
	{
		if (_drainingChildren)
			return;

		_drainingChildren = true;
		List<Exception>? errors = null;
		try
		{
			while (_pendingChildren.TryDequeue(out var child))
			{
				try
				{
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
		}
		finally
		{
			_drainingChildren = false;
		}

		if (errors is not null)
		{
			if (callbackError is not null)
				errors.Insert(0, callbackError);

			throw new AggregateException("Deferred child lifecycle setup failed.", errors);
		}

		if (callbackError is not null)
			ExceptionDispatchInfo.Capture(callbackError).Throw();
	}

	private void DetachChild(Container child)
	{
		if (_state == State.Initializing || _dispatching)
			throw new InvalidOperationException("Child lifecycle detachment requires a callback-free lifecycle boundary.");

		foreach (var phase in _phases.Values)
			phase.Detach(child);

		_initializables.Detach(child);
		child.ChildBuilt = null;
		child.Closing = null;
	}

	private static void InitializeChild(Container child, IInitializable[] participants)
	{
		child.BeginOperation();
		try
		{
			foreach (var participant in participants)
				participant.Initialize();
		}
		finally
		{
			child.EndOperation();
		}
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
	/// <exception cref="AggregateException">Deferred child setup or cleanup fails.</exception>
	/// <exception cref="ObjectDisposedException">The manager or its owning container is disposed.</exception>
	/// <exception cref="InvalidOperationException">Initialization has already started, succeeded, or failed.</exception>
	/// <remarks>
	/// Initial participant exceptions propagate unchanged and leave dispatch disabled; initialization cannot be retried.
	/// Children created during initialization join after the initial traversal. Deferred child failures dispose those
	/// children and leave the initialized manager usable. Already performed initialization is not rolled back.
	/// Participant ownership remains with their containers or external owners.
	/// </remarks>
	public void Initialize()
	{
		ThrowIfDisposed();
		if (_state != State.Configuring || _attachingChildren != 0)
			throw new InvalidOperationException("Initialization can only be attempted once, outside child setup.");

		_source?.BeginOperation();
		_state = State.Initializing;
		try
		{
			foreach (var participant in _initializables.Root)
				participant.Initialize();

			foreach (var (child, participants) in _initializables.Children)
				InitializeChild(child, participants);

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

		DrainChildren();
	}

	/// <summary>Dispatches the default IFrameTickable phase.</summary>
	/// <param name="deltaTime">Elapsed frame time in seconds, supplied by the caller.</param>
	/// <exception cref="ObjectDisposedException">The manager or its owning container is disposed.</exception>
	/// <exception cref="InvalidOperationException">Initialization has not succeeded, or a phase is already being dispatched.</exception>
	/// <exception cref="AggregateException">Deferred child setup or cleanup fails; any dispatch exception is included.</exception>
	/// <remarks>Participant exceptions stop this dispatch. Deferred children join after traversal. Later dispatches remain allowed.</remarks>
	public void Tick(double deltaTime) => Dispatch<IFrameTickable, double>(deltaTime);

	/// <summary>Invokes a registered phase for all of its participants.</summary>
	/// <typeparam name="TPhase">The registered participant interface.</typeparam>
	/// <typeparam name="TArgument">The argument type used when the phase was registered.</typeparam>
	/// <param name="argument">The value passed to each participant.</param>
	/// <exception cref="ObjectDisposedException">The manager or its owning container is disposed.</exception>
	/// <exception cref="InvalidOperationException">
	/// Initialization has not succeeded, dispatch is recursive, the phase is unregistered, or its argument type does not match.
	/// </exception>
	/// <exception cref="AggregateException">Deferred child setup or cleanup fails; any dispatch exception is included.</exception>
	/// <remarks>
	/// Participant exceptions stop this dispatch. Children created during callbacks join after traversal,
	/// before this method returns, and participate starting with the next phase call. Participant order is unspecified.
	/// </remarks>
	public void Dispatch<TPhase, TArgument>(TArgument argument) where TPhase : class
	{
		ThrowIfDisposed();
		if (_state != State.Ready)
			throw new InvalidOperationException("Successful initialization is required before dispatch.");

		if (_dispatching || _attachingChildren != 0 || _drainingChildren)
			throw new InvalidOperationException("Recursive lifecycle dispatch is unsupported.");

		if (!_phases.TryGetValue(typeof(TPhase).TypeHandle, out var entry) || entry is not Phase<TPhase, TArgument> phase)
			throw new InvalidOperationException($"Phase {typeof(TPhase)} is not registered with argument type {typeof(TArgument)}.");

		_source?.BeginOperation();
		_dispatching = true;
		Exception? callbackError = null;
		try
		{
			phase.Dispatch(argument);
		}
		catch (Exception error)
		{
			callbackError = error;
		}
		finally
		{
			_dispatching = false;
			_source?.EndOperation();
		}
		DrainChildren(callbackError);
	}

	/// <summary>Stops dispatch and releases references without disposing participants.</summary>
	/// <exception cref="InvalidOperationException">Initialization or dispatch is currently running.</exception>
	/// <remarks>Repeated calls have no effect.</remarks>
	public void Dispose()
	{
		if (_state == State.Disposed)
			return;

		if (_state == State.Initializing || _dispatching || _attachingChildren != 0 || _drainingChildren)
			throw new InvalidOperationException("Cannot dispose during a lifecycle callback.");

		_state = State.Disposed;
		_source!.ChildBuilt = null;
		foreach (var child in _initializables.Children.Keys)
		{
			child.ChildBuilt = null;
			child.Closing = null;
		}
		_pendingChildren.Clear();
		_initializables = new Participants<IInitializable>([]);
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
