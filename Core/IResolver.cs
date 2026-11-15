namespace Injectra;

/// <summary>Resolves registered services and their dependencies.</summary>
public interface IResolver
{
	/// <summary>Gets an instance of a registered service according to its lifetime.</summary>
	/// <typeparam name="T">The service contract to resolve.</typeparam>
	/// <returns>The resolved service instance.</returns>
	/// <exception cref="ResolutionException">
	/// A requested service is not registered or is ambiguous, a dynamic dependency cycle is detected, a factory returns null,
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
	/// A requested service is not registered or is ambiguous, a dynamic dependency cycle is detected, a factory returns null,
	/// generic arguments violate the implementation constraints, or the requested type contains unbound generic parameters.
	/// </exception>
	/// <exception cref="RegistrationException">A lazily prepared dependency graph is invalid or generic expansion exceeds 128 levels.</exception>
	/// <exception cref="ObjectDisposedException">The container was disposed.</exception>
	/// <remarks>Exceptions from user constructors and factories propagate without being wrapped.</remarks>
	object Resolve(Type serviceType);

	/// <summary>Resolves all registrations of a service contract, or returns an empty list if none exist.</summary>
	/// <typeparam name="T">The closed service contract.</typeparam>
	/// <returns>A new result list for registered services, using each registration's lifetime.</returns>
	/// <exception cref="ObjectDisposedException">The container or a required owner was disposed.</exception>
	/// <exception cref="ResolutionException">A dynamic cycle, null factory result or invalid generic closure is encountered.</exception>
	/// <exception cref="RegistrationException">A lazily prepared graph is invalid.</exception>
	/// <remarks>Local registrations override inherited registrations as a group. Result order is unspecified. User exceptions propagate.</remarks>
	IReadOnlyList<T> ResolveAll<T>();

	/// <summary>Resolves all registrations of a closed service contract.</summary>
	/// <param name="serviceType">The closed contract.</param>
	/// <returns>A new result list, or an empty list if the contract is unregistered.</returns>
	/// <exception cref="ArgumentNullException">The contract is null.</exception>
	/// <exception cref="ObjectDisposedException">The container or a required owner was disposed.</exception>
	/// <exception cref="ResolutionException">The contract is open, a generic closure is invalid, a factory returns null, or a dynamic cycle occurs.</exception>
	/// <exception cref="RegistrationException">A lazily prepared graph is invalid.</exception>
	/// <remarks>Local registrations override inherited registrations as a group. Result order is unspecified. User exceptions propagate.</remarks>
	IReadOnlyList<object> ResolveAll(Type serviceType);
}
