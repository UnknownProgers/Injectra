using Injectra.Internal.Activation;
using Injectra.Internal.Resolution;

namespace Injectra;

/// <summary>A container and its owned singleton services. Concurrent use is unsupported.</summary>
public sealed class Container : IResolver, IDisposable
{
	private Container? _parent;
	// Local registrations only; ancestors keep their own tables.
	private readonly Dictionary<RuntimeTypeHandle, Binding> _bindings;
	private readonly List<Container> _children = [];
	private readonly List<Binding> _path = [];
	private readonly HashSet<object> _externalInstances = new(ReferenceEqualityComparer.Instance);
	private readonly HashSet<object> _ownedInstances = new(ReferenceEqualityComparer.Instance);
	private readonly List<IDisposable> _disposables = [];
	private readonly List<Binding> _preparing = [];
	private readonly Binding _resolverBinding;
	// Canonical inherited binding -> child plan. Aliases share a single plan.
	private Dictionary<Binding, Binding>? _inheritedBindings;
	// Inherited and closed generic contracts requested by this container.
	private Dictionary<RuntimeTypeHandle, Binding>? _lookupCache;

	private Dictionary<(Binding Definition, Type Implementation), Binding>? _closedGenerics;
	private bool _disposed;

	internal Container(Dictionary<RuntimeTypeHandle, Binding> bindings, Container? parent = null)
	{
		_bindings = bindings;
		_parent = parent;
		_resolverBinding = new Binding(typeof(IResolver), Lifetime.Singleton, this) { Owner = this, Prepared = true };
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
		return (T)ResolveService(typeof(T));
	}

	/// <inheritdoc />
	public object Resolve(Type serviceType)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(serviceType);
		if (_bindings.TryGetValue(serviceType.TypeHandle, out var local) && local.Prepared)
			return local.Instance ?? ResolveBinding(local);

		if (serviceType.ContainsGenericParameters)
			throw new ResolutionException("An open generic type cannot be resolved directly.");

		return ResolveService(serviceType);
	}

	private object ResolveService(Type serviceType)
	{
		var binding = FindBinding(serviceType)
			?? throw new ResolutionException($"Service is not registered: {FormatPath(serviceType)}.");
		if (!binding.Prepared)
			PrepareBinding(binding);

		if (ReferenceEquals(binding.Owner, this) && binding.Instance is { } instance)
			return instance;

		return ResolveBinding(binding);
	}

	internal void PrepareBindings()
	{
		foreach (var binding in _bindings.Values)
			PrepareBinding(binding);
	}

	private Binding? FindParentBinding(Type type)
	{
		for (var current = _parent; current is not null; current = current._parent)
			if (current.FindLocalBinding(type) is { } binding)
				return binding;

		return null;
	}

	private Binding? FindLocalBinding(Type type)
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
		var handle = type.TypeHandle;
		if (handle.Equals(typeof(IResolver).TypeHandle))
			return _resolverBinding;

		if (_bindings.TryGetValue(handle, out var local))
			return local;

		if (_lookupCache is not null && _lookupCache.TryGetValue(handle, out var cached))
			return cached;

		var source = FindLocalBinding(type) ?? FindParentBinding(type);
		if (source is null)
			return null;

		Binding binding;
		if (source.ImplementationType.IsGenericTypeDefinition)
			binding = CloseGeneric(source, type);
		else if (source.Lifetime == Lifetime.Singleton)
			binding = source;
		else
			binding = InheritBinding(source);

		// Recursive preparation may return an unfinished binding solely for cycle detection.
		if (binding.Prepared)
			(_lookupCache ??= []).TryAdd(handle, binding);

		return binding;
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
				var dependency = FindBinding(types[i])
					?? throw new RegistrationException($"Service is not registered: {PreparationPath(types[i])}.");
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
	/// <exception cref="InvalidOperationException">This container or a descendant is currently creating a service.</exception>
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
		if (_path.Count != 0)
			throw new InvalidOperationException("A container cannot be disposed while it is creating a service.");

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
