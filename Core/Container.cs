using Injectra.Internal.Resolution;

namespace Injectra;

/// <summary>A container intended for use on one thread; concurrent resolution is unsupported.</summary>
public sealed class Container : IResolver
{
	private readonly Dictionary<Type, Binding> _bindings;
	private readonly List<Binding> _path = [];

	internal Container(Dictionary<Type, Binding> bindings) => _bindings = bindings;

	/// <inheritdoc />
	public T Resolve<T>() => (T)Resolve(typeof(T));

	/// <inheritdoc />
	public object Resolve(Type serviceType)
	{
		ArgumentNullException.ThrowIfNull(serviceType);
		if (serviceType == typeof(IResolver))
			return this;

		if (!_bindings.TryGetValue(serviceType, out var binding))
			throw new ResolutionException($"Service is not registered: {FormatPath(serviceType)}.");

		return ResolveBinding(binding);
	}

	internal object ResolveBinding(Binding binding)
	{
		if (binding.Instance is { } instance)
			return instance;

		if (_path.Contains(binding))
			throw new ResolutionException($"Circular dependency: {FormatPath(binding.ImplementationType)}.");

		_path.Add(binding);

		try
		{
			instance = binding.Activation.Create(this);
			if (binding.Lifetime == Lifetime.Singleton)
				binding.Instance = instance;

			return instance;
		}
		finally
		{
			_path.RemoveAt(_path.Count - 1);
		}
	}

	internal string FormatPath(Type? requested = null)
	{
		var names = _path.Select(binding => binding.ImplementationType.Name);
		return string.Join(" -> ", requested is null ? names : names.Append(requested.Name));
	}
}
