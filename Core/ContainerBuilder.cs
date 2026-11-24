using Injectra.Internal.Activation;
using Injectra.Internal.Resolution;

namespace Injectra;

/// <summary>Collects registrations and builds a container once.</summary>
/// <remarks>This builder is not thread-safe. Validation failures can be corrected and retried; eager activation failures consume the builder.</remarks>
public sealed class ContainerBuilder
{
	private readonly List<RegistrationBuilder> _registrations = [];
	private bool _built;
	private readonly Container? _parent;
	private bool _configuringChild;

	private readonly string? _name;

	/// <summary>Creates a builder for a root container.</summary>
	/// <param name="name">Optional diagnostic name. Defaults to null.</param>
	public ContainerBuilder(string? name = null)
	{
		_name = name;
	}

	internal ContainerBuilder(Container parent, string? name)
	{
		_name = name;
		_parent = parent;
		_configuringChild = true;
	}

	internal Container BuildChild(Action<Container>? prepare = null)
	{
		_configuringChild = false;
		return BuildCore(prepare);
	}

	/// <summary>Registers a type for constructor-based creation.</summary>
	/// <typeparam name="T">The implementation type to register.</typeparam>
	/// <param name="lifetime">The lifetime of instances created for this registration.</param>
	/// <returns>A builder for selecting the service contracts exposed by this registration.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="lifetime"/> is not a defined lifetime.</exception>
	/// <exception cref="InvalidOperationException">This builder has already built a container successfully.</exception>
	public RegistrationBuilder Register<T>(Lifetime lifetime) where T : class => Add(typeof(T), lifetime);

	/// <summary>Registers a concrete type or an open generic type definition.</summary>
	/// <param name="implementationType">The implementation type.</param>
	/// <param name="lifetime">The instance lifetime.</param>
	/// <returns>A fluent registration builder.</returns>
	/// <exception cref="ArgumentNullException">The type is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException">The lifetime is invalid.</exception>
	/// <exception cref="InvalidOperationException">The builder has completed.</exception>
	public RegistrationBuilder Register(Type implementationType, Lifetime lifetime) => Add(implementationType, lifetime);

	/// <summary>Registers a service created by a factory.</summary>
	/// <param name="factory">A factory called during resolution, not during registration or build.</param>
	/// <typeparam name="T">The implementation type to register.</typeparam>
	/// <param name="lifetime">The lifetime of instances created for this registration.</param>
	/// <returns>A builder for selecting the service contracts exposed by this registration.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="lifetime"/> is not a defined lifetime.</exception>
	/// <exception cref="InvalidOperationException">This builder has already built a container successfully.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
	/// <remarks>The factory must return a non-null instance. Its dependencies are validated when it runs.</remarks>
	public RegistrationBuilder Register<T>(Func<IResolver, T> factory, Lifetime lifetime) where T : class
	{
		ArgumentNullException.ThrowIfNull(factory);
		return Add(typeof(T), lifetime, factory);
	}

	/// <summary>Registers an existing instance, externally owned unless ownership is explicitly transferred.</summary>
	/// <typeparam name="T">The type whose contracts can be exposed.</typeparam>
	/// <param name="instance">The instance to register.</param>
	/// <returns>A builder for selecting the service contracts exposed by this instance.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="instance"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">This builder has already built a container successfully.</exception>
	public RegistrationBuilder RegisterInstance<T>(T instance) where T : class
	{
		ArgumentNullException.ThrowIfNull(instance);
		return Add(typeof(T), Lifetime.Singleton, instance: instance);
	}

	/// <summary>Validates registrations, prepares dependencies, and creates explicitly NonLazy singletons.</summary>
	/// <returns>A container with fixed registrations and prepared constructor dependencies.</returns>
	/// <exception cref="InvalidOperationException">This builder has already built successfully, or Build is called inside a CreateChild configuration callback.</exception>
	/// <exception cref="RegistrationException">
	/// A concrete contract is registered more than once; a registration replaces the reserved <see cref="IResolver"/> contract;
	/// a constructor-based implementation is not concrete or does not have exactly one public constructor;
	/// the constructor dependency graph contains a missing or ambiguous dependency or a cycle; or an ancestor instance is transferred to a child.
	/// </exception>
	/// <exception cref="ObjectDisposedException">The parent container has been disposed.</exception>
	/// <exception cref="ResolutionException">Eager creation encounters a dynamic dependency cycle or a factory returns null.</exception>
	/// <exception cref="AggregateException">Eager creation fails and disposal of the failed container also fails.</exception>
	/// <remarks>
	/// Local constructor graphs and their dependencies are prepared before eager creation.
	/// Other inherited transient graphs are prepared when requested.
	/// The builder is cleared and locked before eager creation starts. Validation failures can be retried;
	/// eager activation failures consume the builder and dispose the failed container.
	/// User constructor and factory exceptions propagate unchanged unless cleanup also fails.
	/// Existing instances remain externally owned unless OwnedByContainer is selected.
	/// </remarks>
	public Container Build() => BuildCore(null);

	/// <summary>Builds a container and prepares it before its children can join lifecycle dispatch.</summary>
	/// <param name="prepare">Called after eager creation, with the built container available for resolution.</param>
	/// <returns>The prepared container.</returns>
	/// <exception cref="ArgumentNullException">The preparation callback is null.</exception>
	/// <exception cref="InvalidOperationException">The builder has completed or is inside child configuration.</exception>
	/// <exception cref="RegistrationException">Registrations or constructor dependencies are invalid.</exception>
	/// <exception cref="ResolutionException">Eager creation or preparation encounters a resolution error.</exception>
	/// <exception cref="ObjectDisposedException">The parent or prepared container has been disposed.</exception>
	/// <exception cref="AggregateException">Preparation or activation fails and cleanup also fails.</exception>
	/// <remarks>Callback exceptions propagate. Failure disposes the container. Initialize remains an explicit operation after Build returns.</remarks>
	public Container Build(Action<Container> prepare)
	{
		ArgumentNullException.ThrowIfNull(prepare);
		return BuildCore(prepare);
	}

	private Container BuildCore(Action<Container>? prepare)
	{
		EnsureMutable();
		if (_configuringChild)
			throw new InvalidOperationException("CreateChild builds the child after configuration.");

		_parent?.ThrowIfDisposed();
		var registrations = _registrations.Select(draft => draft.Snapshot()).ToArray();
		var bindings = new Dictionary<RuntimeTypeHandle, Binding[]>();
		var container = new Container(bindings, _parent, _name);
		List<Binding>? eager = null;
		foreach (var registration in registrations)
		{
			var binding = new Binding(registration.ImplementationType, registration.Lifetime, registration.Instance) { Owner = container };
			if (registration.Instance is null)
			{
				binding.Activation = registration.Factory is { } factory
					? new FactoryActivation(factory)
					: registration.Activation!;
			}

			if (registration.NonLazy)
				(eager ??= []).Add(binding);

			var registered = new[] { binding };
			foreach (var contract in registration.Contracts)
			{
				if (contract.TypeHandle.Equals(typeof(IResolver).TypeHandle))
					throw new RegistrationException("IResolver is reserved for the current container.");

				if (bindings.TryAdd(contract.TypeHandle, registered))
					continue;

				if (!contract.IsInterface)
					throw new RegistrationException($"Service {contract} is already registered.");

				bindings[contract.TypeHandle] = [.. bindings[contract.TypeHandle], binding];
			}
		}

		container.PrepareBindings();
		foreach (var registration in registrations)
		{
			if (registration.Instance is not { } instance)
				continue;

			if (registration.OwnsInstance)
			{
				_parent?.EnsureInstanceCanBeOwned(instance);
				container.RegisterOwnedInstance(instance);
			}
			else
			{
				container.RegisterExternalInstance(instance);
			}
		}

		_parent?.AttachChild(container);
		Complete();

		if (eager is not null || prepare is not null)
			PrepareContainer(container, eager, prepare);
		else
			container.CompletePreparation();

		return container;
	}

	private static void PrepareContainer(Container container, List<Binding>? bindings, Action<Container>? prepare)
	{
		try
		{
			if (bindings is not null)
				foreach (var binding in bindings)
					container.ResolveBinding(binding);
			prepare?.Invoke(container);
			container.CompletePreparation();
		}
		catch (Exception setupError)
		{
			try
			{
				container.Dispose();
			}
			catch (Exception cleanupError)
			{
				throw new AggregateException("Container activation or preparation and cleanup failed.", setupError, cleanupError);
			}
			throw;
		}
	}

	private void Complete()
	{
		_built = true;
		foreach (var draft in _registrations)
			draft.Complete();

		_registrations.Clear();
	}

	private RegistrationBuilder Add(Type implementationType, Lifetime lifetime, Func<IResolver, object>? factory = null, object? instance = null)
	{
		EnsureMutable();
		ArgumentNullException.ThrowIfNull(implementationType);
		if (!Enum.IsDefined(lifetime))
			throw new ArgumentOutOfRangeException(nameof(lifetime));

		var registration = new RegistrationBuilder(implementationType, lifetime, factory, instance);
		_registrations.Add(registration);
		return registration;
	}

	private void EnsureMutable()
	{
		if (_built)
			throw new InvalidOperationException("ContainerBuilder has already built a container.");
	}
}
