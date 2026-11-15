using System.ComponentModel;
using Injectra.Internal.Resolution;

namespace Injectra.Generation;

/// <summary>Provides prelinked constructor arguments to generated activation code.</summary>
/// <remarks>Infrastructure for Injectra's generator. Contexts are supplied by the container and must not be retained.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct ActivationContext
{
	private readonly Container _container;
	private readonly Binding[] _dependencies;

	internal ActivationContext(Container container, Binding[] dependencies)
	{
		_container = container;
		_dependencies = dependencies;
	}

	/// <summary>Resolves a constructor argument from its prepared binding.</summary>
	/// <typeparam name="T">The constructor parameter type.</typeparam>
	/// <param name="index">The zero-based constructor parameter index.</param>
	/// <returns>The argument according to its registered lifetime.</returns>
	/// <exception cref="IndexOutOfRangeException">The index is outside the constructor's parameter list.</exception>
	/// <exception cref="ObjectDisposedException">The binding's container was disposed.</exception>
	/// <exception cref="ResolutionException">A dynamic dependency cycle is detected or a factory returns null.</exception>
	/// <remarks>User constructor and factory exceptions propagate unchanged. Only container-supplied contexts are valid.</remarks>
	public T Resolve<T>(int index)
	{
		_container.ThrowIfDisposed();
		var binding = _dependencies[index];
		if (binding.Instance is { } instance)
		{
			binding.Owner.ThrowIfDisposed();
			return (T)instance;
		}

		return (T)_container.ResolveBinding(binding);
	}
}
