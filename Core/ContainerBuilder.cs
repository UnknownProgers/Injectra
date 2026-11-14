using Injectra.Internal.Activation;
using Injectra.Internal.Registration;
using Injectra.Internal.Resolution;

namespace Injectra;

/// <summary>Collects registrations and builds a container once.</summary>
/// <remarks>This builder is not thread-safe. A failed build can be corrected and retried.</remarks>
public sealed class ContainerBuilder
{
	private readonly List<IRegistrationBuilder> _registrations = [];
	private bool _built;

	/// <summary>Registers a type for constructor-based creation.</summary>
	/// <typeparam name="T">The implementation type to register.</typeparam>
	/// <param name="lifetime">The lifetime of instances created for this registration.</param>
	/// <returns>A builder for selecting the service contracts exposed by this registration.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="lifetime"/> is not a defined lifetime.</exception>
	/// <exception cref="InvalidOperationException">This builder has already built a container successfully.</exception>
	public RegistrationBuilder<T> Register<T>(Lifetime lifetime) where T : class => Add<T>(lifetime);

	/// <summary>Registers a service created by a factory.</summary>
	/// <param name="factory">A factory called during resolution, not during registration or build.</param>
	/// <typeparam name="T">The implementation type to register.</typeparam>
	/// <param name="lifetime">The lifetime of instances created for this registration.</param>
	/// <returns>A builder for selecting the service contracts exposed by this registration.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="lifetime"/> is not a defined lifetime.</exception>
	/// <exception cref="InvalidOperationException">This builder has already built a container successfully.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
	/// <remarks>The factory must return a non-null instance. Its dependencies are validated when it runs.</remarks>
	public RegistrationBuilder<T> Register<T>(Func<IResolver, T> factory, Lifetime lifetime) where T : class
	{
		ArgumentNullException.ThrowIfNull(factory);
		return Add<T>(lifetime, factory);
	}

	/// <summary>Registers an existing instance that is returned for every resolution of its contracts.</summary>
	/// <typeparam name="T">The type whose contracts can be exposed.</typeparam>
	/// <param name="instance">The instance to register.</param>
	/// <returns>A builder for selecting the service contracts exposed by this instance.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="instance"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">This builder has already built a container successfully.</exception>
	public RegistrationBuilder<T> RegisterInstance<T>(T instance) where T : class
	{
		ArgumentNullException.ThrowIfNull(instance);
		return Add(Lifetime.Singleton, instance: instance);
	}

	/// <summary>Validates registrations and creates a container without invoking constructors or factories.</summary>
	/// <returns>A container with fixed registrations and prepared constructor dependencies.</returns>
	/// <exception cref="InvalidOperationException">This builder has already built a container successfully.</exception>
	/// <exception cref="RegistrationException">
	/// A contract is registered more than once; a registration replaces the reserved <see cref="IResolver"/> contract;
	/// a constructor-based implementation is not concrete or does not have exactly one public constructor;
	/// or the constructor dependency graph contains a missing dependency or a cycle.
	/// </exception>
	/// <remarks>A successful build clears and locks the builder and its registration builders.</remarks>
	public Container Build()
	{
		EnsureMutable();
		var entries = _registrations.Select(draft => new BuildEntry(draft.Snapshot())).ToArray();
		var contracts = CollectContracts(entries);
		LinkAndValidate(entries, contracts);

		var container = new Container(contracts.ToDictionary(pair => pair.Key, pair => pair.Value.Binding));
		var resolverBinding = new Binding(typeof(IResolver), Lifetime.Singleton, container);
		foreach (var entry in entries)
			PrepareActivation(entry, resolverBinding);

		Complete();
		return container;
	}

	private static Dictionary<Type, BuildEntry> CollectContracts(BuildEntry[] entries)
	{
		var contracts = new Dictionary<Type, BuildEntry>();
		foreach (var entry in entries)
		{
			foreach (var contract in entry.Registration.Contracts)
			{
				if (contract == typeof(IResolver))
					throw new RegistrationException("IResolver is reserved for the current container.");

				if (!contracts.TryAdd(contract, entry))
					throw new RegistrationException($"Service {contract} is already registered.");
			}
		}
		return contracts;
	}

	private static void LinkAndValidate(BuildEntry[] entries, Dictionary<Type, BuildEntry> contracts)
	{
		var visited = new HashSet<BuildEntry>();
		var path = new List<BuildEntry>();
		foreach (var entry in entries)
			Visit(entry, contracts, visited, path);
	}

	private static void Visit(BuildEntry entry,
		Dictionary<Type, BuildEntry> contracts,
		HashSet<BuildEntry> visited,
		List<BuildEntry> path)
	{
		if (visited.Contains(entry))
			return;

		if (path.Contains(entry))
			throw new RegistrationException($"Circular dependency: {FormatPath(path, entry.Registration.ImplementationType)}.");

		path.Add(entry);
		var parameters = entry.Registration.Constructor?.GetParameters() ?? [];
		entry.Dependencies = new Binding?[parameters.Length];
		for (var i = 0; i < parameters.Length; i++)
		{
			var type = parameters[i].ParameterType;
			// A null slot represents the built-in resolver, supplied when the container exists.
			if (type == typeof(IResolver))
				continue;

			if (!contracts.TryGetValue(type, out var dependency))
				throw new RegistrationException($"Service is not registered: {FormatPath(path, type)}.");

			entry.Dependencies[i] = dependency.Binding;
			Visit(dependency, contracts, visited, path);
		}
		path.RemoveAt(path.Count - 1);
		visited.Add(entry);
	}

	private static void PrepareActivation(BuildEntry entry, Binding resolverBinding)
	{
		var registration = entry.Registration;
		if (registration.Instance is not null)
			return;

		entry.Binding.Activation = registration.Factory is { } factory
			? new FactoryActivation(factory)
			: new ConstructorActivation(
				registration.Constructor!,
				entry.Dependencies.Select(binding => binding ?? resolverBinding).ToArray()
			);
	}

	private void Complete()
	{
		_built = true;
		foreach (var draft in _registrations)
			draft.Complete();

		_registrations.Clear();
	}

	private static string FormatPath(List<BuildEntry> path, Type type) =>
		string.Join(" -> ", path.Select(entry => entry.Registration.ImplementationType.Name).Append(type.Name));

	private RegistrationBuilder<T> Add<T>(Lifetime lifetime, Func<IResolver, object>? factory = null, T? instance = null) where T : class
	{
		EnsureMutable();
		if (!Enum.IsDefined(lifetime))
			throw new ArgumentOutOfRangeException(nameof(lifetime));

		var registration = new RegistrationBuilder<T>(lifetime, factory, instance);
		_registrations.Add(registration);
		return registration;
	}

	private void EnsureMutable()
	{
		if (_built)
			throw new InvalidOperationException("ContainerBuilder has already built a container.");
	}

	private sealed class BuildEntry(Registration registration)
	{
		internal Registration Registration { get; } = registration;
		internal Binding Binding { get; } = new(registration.ImplementationType, registration.Lifetime, registration.Instance);
		internal Binding?[] Dependencies { get; set; } = [];
	}
}
