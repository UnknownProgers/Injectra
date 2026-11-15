namespace Injectra;

/// <summary>Resolves registered services and their dependencies.</summary>
public interface IResolver
{
	/// <summary>Gets an instance of a registered service according to its lifetime.</summary>
	/// <typeparam name="T">The service contract to resolve.</typeparam>
	/// <returns>The resolved service instance.</returns>
	/// <exception cref="ResolutionException">
	/// A requested service is not registered, a dynamic dependency cycle is detected, a factory returns null,
	/// or generic arguments violate the implementation constraints.
	/// </exception>
	/// <exception cref="RegistrationException">A lazily prepared dependency graph is invalid or generic expansion exceeds 128 levels.</exception>
	/// <exception cref="ObjectDisposedException">The container was disposed.</exception>
	/// <remarks>Exceptions from user constructors and factories propagate without being wrapped.</remarks>
	T Resolve<T>();

	/// <summary>Gets an instance of a registered service according to its lifetime.</summary>
	/// <param name="serviceType">The closed service contract to resolve.</param>
	/// <returns>The resolved service instance.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="serviceType"/> is <see langword="null"/>.</exception>
	/// <exception cref="ResolutionException">
	/// A requested service is not registered, a dynamic dependency cycle is detected, a factory returns null,
	/// generic arguments violate the implementation constraints, or the requested type contains unbound generic parameters.
	/// </exception>
	/// <exception cref="RegistrationException">A lazily prepared dependency graph is invalid or generic expansion exceeds 128 levels.</exception>
	/// <exception cref="ObjectDisposedException">The container was disposed.</exception>
	/// <remarks>Exceptions from user constructors and factories propagate without being wrapped.</remarks>
	object Resolve(Type serviceType);
}
