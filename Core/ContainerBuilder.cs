using Injectra.Internal.Activation;
using Injectra.Internal.Resolution;

namespace Injectra;

/// <summary>Collects registrations and builds a container once.</summary>
/// <remarks>This builder is not thread-safe. A failed build can be corrected and retried.</remarks>
public sealed class ContainerBuilder
{
	private readonly List<RegistrationBuilder> _registrations = [];
	private bool _built;
	private readonly Container? _parent;
	private bool _configuringChild;

	/// <summary>Creates a builder for a root container.</summary>
	public ContainerBuilder()
	{
	}

	internal ContainerBuilder(Container parent)
	{
		_parent = parent;
		_configuringChild = true;
	}

	internal Container BuildChild()
	{
		_configuringChild = false;
		return Build();
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

	/// <summary>Validates registrations and creates a container without invoking constructors or factories.</summary>
	/// <returns>A container with fixed registrations and prepared constructor dependencies.</returns>
	/// <exception cref="InvalidOperationException">This builder has already built successfully, or Build is called inside a CreateChild configuration callback.</exception>
	/// <exception cref="RegistrationException">
	/// A contract is registered more than once; a registration replaces the reserved <see cref="IResolver"/> contract;
	/// a constructor-based implementation is not concrete or does not have exactly one public constructor;
	/// the constructor dependency graph contains a missing dependency or a cycle; or an ancestor instance is transferred to a child.
	/// </exception>
	/// <exception cref="ObjectDisposedException">The parent container has been disposed.</exception>
	/// <remarks>
	/// Local constructor graphs and their dependencies are prepared during build.
	/// Other inherited transient graphs are prepared when requested.
	/// A successful build clears and locks the builder and its registration builders.
	/// Existing instances remain externally owned unless OwnedByContainer is selected.
	/// </remarks>
	public Container Build()
	{
		EnsureMutable();
		if (_configuringChild)
			throw new InvalidOperationException("CreateChild builds the child after configuration.");

		_parent?.ThrowIfDisposed();
		var registrations = _registrations.Select(draft => draft.Snapshot()).ToArray();
		var bindings = new Dictionary<Type, Binding>();
		var container = new Container(bindings, _parent);
		foreach (var registration in registrations)
		{
			var binding = new Binding(registration.ImplementationType, registration.Lifetime, registration.Instance) { Owner = container };
			if (registration.Instance is null)
			{
				binding.Activation = registration.Factory is { } factory
					? new FactoryActivation(factory)
					: new ConstructorActivation(registration.Constructor!, []);
			}

			foreach (var contract in registration.Contracts)
			{
				if (contract == typeof(IResolver))
					throw new RegistrationException("IResolver is reserved for the current container.");

				if (!bindings.TryAdd(contract, binding))
					throw new RegistrationException($"Service {contract} is already registered.");
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
		return container;
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
