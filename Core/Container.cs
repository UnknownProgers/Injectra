using Injectra.Internal.Activation;
using Injectra.Internal.Resolution;

namespace Injectra;

/// <summary>A container and its owned singleton services. Concurrent use is unsupported.</summary>
public sealed class Container : IResolver, IDisposable
{
	private Container? _parent;
	// Local registrations only; ancestors keep their own tables.
	private readonly Dictionary<RuntimeTypeHandle, Binding[]> _bindings;
	private readonly List<Container> _children = [];
	private readonly List<Binding> _path = [];
	private readonly HashSet<object> _externalInstances = new(ReferenceEqualityComparer.Instance);
	private readonly HashSet<object> _ownedInstances = new(ReferenceEqualityComparer.Instance);
	private readonly List<IDisposable> _disposables = [];
	private readonly List<Binding> _preparing = [];
	private readonly Binding[] _resolverBindings;
	// Canonical inherited binding -> child plan. Aliases share a single plan.
	private Dictionary<Binding, Binding>? _inheritedBindings;
	// Inherited and closed generic contracts requested by this container.
	private Dictionary<RuntimeTypeHandle, Binding[]>? _lookupCache;

	private Dictionary<(Binding Definition, Type Implementation), Binding>? _closedGenerics;
	private bool _disposed;
	private int _activeOperations;
	internal bool IsRoot => _parent is null;

	internal Container(Dictionary<RuntimeTypeHandle, Binding[]> bindings, Container? parent = null)
	{
		_bindings = bindings;
		_parent = parent;
		_resolverBindings = [new Binding(typeof(IResolver), Lifetime.Singleton, this) { Owner = this, Prepared = true }];
	}

	/// <summary>Builds a child container with optional local registrations and inherited parent contracts.</summary>
	/// <param name="configure">Configures local registrations before the child is built.</param>
	/// <returns>A child container owned by this container.</returns>
	/// <exception cref="ObjectDisposedException">This container was disposed.</exception>
	/// <exception cref="RegistrationException">The child's registrations or constructor graph are invalid.</exception>
	/// <exception cref="InvalidOperationException">The callback attempts to build its builder directly.</exception>
	/// <remarks>
	/// Callback exceptions propagate. Only local registrations are stored directly.
	/// Inherited contracts are cached on demand. Singletons retain their owner's context;
	/// transient graphs are prepared for this child and use its overrides.
	/// </remarks>
	public Container CreateChild(Action<ContainerBuilder>? configure = null)
	{
		ThrowIfDisposed();
		var builder = new ContainerBuilder(this);
		configure?.Invoke(builder);
		return builder.BuildChild();
	}

	/// <inheritdoc />
	public T Resolve<T>()
	{
		ThrowIfDisposed();
		return (T)ResolveInternal(typeof(T));
	}

	/// <inheritdoc />
	public object Resolve(Type serviceType)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(serviceType);
		if (_bindings.TryGetValue(serviceType.TypeHandle, out var local) && local.Length == 1 && local[0].Prepared)
			return local[0].Instance ?? ResolveBinding(local[0]);

		if (serviceType.ContainsGenericParameters)
			throw new ResolutionException("An open generic type cannot be resolved directly.");

		return ResolveInternal(serviceType);
	}

	private object ResolveInternal(Type serviceType)
	{
		var binding = FindBinding(serviceType)
			?? throw new ResolutionException($"Service is not registered: {FormatPath(serviceType)}.");
		if (!binding.Prepared)
			PrepareBinding(binding);

		if (ReferenceEquals(binding.Owner, this) && binding.Instance is { } instance)
			return instance;

		return ResolveBinding(binding);
	}

	/// <inheritdoc />
	public IReadOnlyList<T> ResolveAll<T>()
	{
		ThrowIfDisposed();
		return ResolveAllInternal<T>(typeof(T));
	}

	/// <inheritdoc />
	public IReadOnlyList<object> ResolveAll(Type serviceType)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(serviceType);
		if (serviceType.ContainsGenericParameters)
			throw new ResolutionException("An open generic type cannot be resolved directly.");

		return ResolveAllInternal<object>(serviceType);
	}

	private IReadOnlyList<T> ResolveAllInternal<T>(Type serviceType)
	{
		var bindings = FindBindings(serviceType);
		if (bindings is null)
			return Array.Empty<T>();

		var results = new T[bindings.Length];
		for (var i = 0; i < bindings.Length; i++)
		{
			if (!bindings[i].Prepared)
				PrepareBinding(bindings[i]);

			results[i] = (T)ResolveBinding(bindings[i]);
		}
		return results;
	}

	internal void EnsureSingletons(Type contract)
	{
		var bindings = FindBindings(contract);
		if (bindings is null)
			return;

		foreach (var binding in bindings)
		{
			if (binding.Lifetime != Lifetime.Singleton)
				throw new RegistrationException($"Contract {contract} requires singleton or instance registrations.");
		}
	}

	internal void BeginOperation()
	{
		ThrowIfDisposed();
		_activeOperations++;
	}

	internal void EndOperation() => _activeOperations--;

	internal void PrepareBindings()
	{
		foreach (var bindings in _bindings.Values)
		foreach (var binding in bindings)
			PrepareBinding(binding);
	}

	private Binding[]? FindParentBindings(Type type)
	{
		for (var current = _parent; current is not null; current = current._parent)
			if (current.FindLocalBindings(type) is { } bindings)
				return bindings;
		return null;
	}

	private Binding[]? FindLocalBindings(Type type)
	{
		if (_bindings.TryGetValue(type.TypeHandle, out var exact))
			return exact;

		if (type.IsConstructedGenericType && _bindings.TryGetValue(type.GetGenericTypeDefinition().TypeHandle, out var open))
			return open;

		return null;
	}

	private Binding CloseGeneric(Binding definition, Type contract)
	{
		var owner = definition.Lifetime == Lifetime.Singleton ? definition.Owner : this;
		if (!ReferenceEquals(owner, this))
			return owner.CloseGeneric(definition, contract);

		Type implementation;
		try
		{
			implementation = definition.ImplementationType.MakeGenericType(contract.GetGenericArguments());
		}
		catch (ArgumentException exception)
		{
			throw new ResolutionException($"Cannot close {definition.ImplementationType} for {contract}: {exception.Message}");
		}

		var key = (definition, implementation);
		if (_closedGenerics is not null && _closedGenerics.TryGetValue(key, out var existing))
			return existing;

		// Expanding dependencies such as A<T> -> A<List<T>> never repeat a closed type.
		if (_preparing.Count >= 128)
			throw new RegistrationException("Generic dependency expansion exceeds the maximum preparation depth of 128.");

		var closed = new Binding(implementation, definition.Lifetime)
		{
			Owner = this,
			Activation = ConstructorActivation.ForType(implementation)
		};
		// Publish first so recursive dependencies reuse the binding and cycle detection can identify it.
		(_closedGenerics ??= []).Add(key, closed);
		try
		{
			PrepareBinding(closed);
			return closed;
		}
		catch
		{
			_closedGenerics.Remove(key);
			throw;
		}
	}

	private Binding? FindBinding(Type type)
	{
		var bindings = FindBindings(type);
		if (bindings is null)
			return null;

		if (bindings.Length != 1)
			throw new ResolutionException($"Multiple registrations for {type}. Use ResolveAll to request all services.");

		return bindings[0];
	}

	private Binding[]? FindBindings(Type type)
	{
		var handle = type.TypeHandle;
		if (handle.Equals(typeof(IResolver).TypeHandle))
			return _resolverBindings;

		if (_bindings.TryGetValue(handle, out var local))
			return local;

		if (_lookupCache is not null && _lookupCache.TryGetValue(handle, out var cached))
			return cached;

		var source = FindLocalBindings(type) ?? FindParentBindings(type);
		if (source is null)
			return null;

		var bindings = new Binding[source.Length];
		for (var i = 0; i < source.Length; i++)
		{
			var binding = source[i];
			if (binding.ImplementationType.IsGenericTypeDefinition)
				bindings[i] = CloseGeneric(binding, type);
			else if (binding.Lifetime == Lifetime.Singleton)
				bindings[i] = binding;
			else
				bindings[i] = InheritBinding(binding);
		}

		if (bindings.All(binding => binding.Prepared))
			(_lookupCache ??= []).TryAdd(handle, bindings);

		return bindings;
	}

	private Binding InheritBinding(Binding source)
	{
		if (_inheritedBindings is not null && _inheritedBindings.TryGetValue(source, out var existing))
			return existing;

		var inherited = new Binding(source.ImplementationType, Lifetime.Transient)
		{
			Owner = this,
			Activation = source.Activation
		};
		// Publish before preparing so recursive dependency edges refer to the same binding.
		(_inheritedBindings ??= []).Add(source, inherited);

		try
		{
			PrepareBinding(inherited);
			return inherited;
		}
		catch
		{
			_inheritedBindings.Remove(source);
			throw;
		}
	}

	private void PrepareBinding(Binding binding)
	{
		if (binding.Prepared || !ReferenceEquals(binding.Owner, this) || binding.ImplementationType.IsGenericTypeDefinition)
			return;

		if (_preparing.Contains(binding))
			throw new RegistrationException($"Circular dependency: {PreparationPath(binding.ImplementationType)}.");

		if (binding.Instance is not null)
		{
			binding.Prepared = true;
			return;
		}

		_preparing.Add(binding);
		try
		{
			var types = binding.Activation.DependencyTypes;
			var dependencies = new Binding[types.Count];
			for (var i = 0; i < types.Count; i++)
			{
				var candidates = FindBindings(types[i])
					?? throw new RegistrationException($"Service is not registered: {PreparationPath(types[i])}.");

				if (candidates.Length != 1)
					throw new RegistrationException($"Multiple registrations for {types[i]}: {PreparationPath(types[i])}.");

				var dependency = candidates[0];
				PrepareBinding(dependency);
				dependencies[i] = dependency;
			}
			binding.Activation = binding.Activation.WithDependencies(dependencies);
			binding.Prepared = true;
		}
		finally
		{
			_preparing.RemoveAt(_preparing.Count - 1);
		}
	}

	internal object ResolveBinding(Binding binding)
	{
		ThrowIfDisposed();
		if (!ReferenceEquals(binding.Owner, this))
			return binding.Owner.ResolveBinding(binding);

		if (binding.Instance is { } instance)
			return instance;

		if (_path.Contains(binding))
			throw new ResolutionException($"Circular dependency: {FormatPath(binding.ImplementationType)}.");

		_path.Add(binding);
		try
		{
			instance = binding.Activation.Create(this);
			if (binding.Lifetime == Lifetime.Singleton)
			{
				TrackOwned(instance);
				binding.Instance = instance;
			}
			return instance;
		}
		finally
		{
			_path.RemoveAt(_path.Count - 1);
		}
	}

	internal void RegisterExternalInstance(object instance)
	{
		if (!_ownedInstances.Contains(instance))
			_externalInstances.Add(instance);
	}

	internal void EnsureInstanceCanBeOwned(object instance)
	{
		for (var container = this; container is not null; container = container._parent)
			if (container._externalInstances.Contains(instance) || container._ownedInstances.Contains(instance))
				throw new RegistrationException("An instance already managed by an ancestor cannot transfer ownership to a child.");
	}

	internal void RegisterOwnedInstance(object instance)
	{
		_externalInstances.Remove(instance);
		TrackOwned(instance);
	}

	private void TrackOwned(object instance)
	{
		// Never claim an instance already managed by an ancestor or registered as external here.
		for (var container = this; container is not null; container = container._parent)
			if (container._externalInstances.Contains(instance) || container._ownedInstances.Contains(instance))
				return;

		_ownedInstances.Add(instance);
		if (instance is IDisposable disposable)
			_disposables.Add(disposable);
	}

	internal void AttachChild(Container child)
	{
		ThrowIfDisposed();
		_children.Add(child);
	}

	internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

	/// <summary>Disposes children, then owned instances in reverse acquisition order, and releases container references.</summary>
	/// <exception cref="AggregateException">One or more owned objects failed to dispose; remaining cleanup still runs.</exception>
	/// <exception cref="InvalidOperationException">This container or a descendant is currently creating a service or running a callback.</exception>
	/// <remarks>Repeated calls do nothing. Transients and externally owned instances are not disposed. Resolution is forbidden during cleanup.</remarks>
	public void Dispose()
	{
		if (_disposed)
			return;

		EnsureNotResolving();
		_disposed = true;
		List<Exception>? errors = null;
		var children = _children.ToArray();
		_children.Clear();

		for (var i = children.Length - 1; i >= 0; i--)
		{
			try
			{
				children[i].Dispose();
			}
			catch (Exception exception)
			{
				(errors ??= []).Add(exception);
			}
		}

		for (var i = _disposables.Count - 1; i >= 0; i--)
		{
			try
			{
				_disposables[i].Dispose();
			}
			catch (Exception exception)
			{
				(errors ??= []).Add(exception);
			}
		}

		_disposables.Clear();
		_ownedInstances.Clear();
		_externalInstances.Clear();
		_bindings.Clear();
		_inheritedBindings?.Clear();
		_lookupCache?.Clear();
		_closedGenerics?.Clear();
		_parent?._children.Remove(this);
		_parent = null;

		if (errors is not null)
			throw new AggregateException(errors);
	}

	private void EnsureNotResolving()
	{
		if (_path.Count != 0 || _activeOperations != 0)
			throw new InvalidOperationException("A container cannot be disposed while it is creating a service or running a callback.");

		foreach (var child in _children)
			child.EnsureNotResolving();
	}

	private string PreparationPath(Type type) => string.Join(" -> ",
		_preparing.Select(binding => binding.ImplementationType.Name).Append(type.Name));

	internal string FormatPath(Type? requested = null)
	{
		var names = _path.Select(binding => binding.ImplementationType.Name);
		return string.Join(" -> ", requested is null ? names : names.Append(requested.Name));
	}
}
