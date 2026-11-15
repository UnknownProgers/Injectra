using Injectra.Internal.Activation;
using Injectra.Internal.Registration;

namespace Injectra;

/// <summary>Selects the service contracts exposed by a single registration.</summary>
/// <remarks>Without explicit contracts, the registration exposes the implementation type itself.</remarks>
public sealed class RegistrationBuilder
{
	private readonly List<Type> _contracts = [];
	private readonly Lifetime _lifetime;
	private Func<IResolver, object>? _factory;
	private object? _instance;
	private readonly Type _implementationType;
	private bool _completed;
	private bool _ownsInstance;
	private bool _nonLazy;

	internal RegistrationBuilder(Type implementationType, Lifetime lifetime, Func<IResolver, object>? factory = null, object? instance = null)
	{
		_implementationType = implementationType;
		_lifetime = lifetime;
		_factory = factory;
		_instance = instance;
	}

	/// <summary>Adds a service contract to this registration.</summary>
	/// <typeparam name="TContract">A type assignable from the implementation type.</typeparam>
	/// <returns>This registration builder for further configuration.</returns>
	/// <exception cref="RegistrationException">
	/// The implementation type is not assignable to <typeparamref name="TContract"/>, or the contract was already added.
	/// </exception>
	/// <exception cref="InvalidOperationException">The owning container builder has already built successfully.</exception>
	/// <remarks>Adding an explicit contract removes the implicit self contract. Use <see cref="AsSelf"/> to expose it too.</remarks>
	public RegistrationBuilder As<TContract>()
	{
		return As(typeof(TContract));
	}

	/// <summary>Adds a closed contract or a positionally matching open generic contract.</summary>
	/// <param name="contract">The service contract.</param>
	/// <returns>This builder.</returns>
	/// <exception cref="ArgumentNullException">The contract is null.</exception>
	/// <exception cref="RegistrationException">The contract is incompatible or duplicated.</exception>
	/// <exception cref="InvalidOperationException">The builder is complete.</exception>
	public RegistrationBuilder As(Type contract)
	{
		EnsureMutable();
		ArgumentNullException.ThrowIfNull(contract);
		if (_implementationType.IsGenericTypeDefinition)
			GenericTypes.ValidateContract(_implementationType, contract);
		else if (!contract.IsAssignableFrom(_implementationType))
			throw new RegistrationException($"{_implementationType} does not implement {contract}.");

		AddContract(contract);
		return this;
	}

	/// <summary>Explicitly exposes the implementation type alongside the other contracts of this registration.</summary>
	/// <returns>This registration builder for further configuration.</returns>
	/// <exception cref="RegistrationException">The self contract was already explicitly added.</exception>
	/// <exception cref="InvalidOperationException">The owning container builder has already built successfully.</exception>
	public RegistrationBuilder AsSelf()
	{
		EnsureMutable();
		AddContract(_implementationType);
		return this;
	}

	/// <summary>Creates this singleton during Build, after registration validation and dependency preparation.</summary>
	/// <returns>This registration builder.</returns>
	/// <exception cref="InvalidOperationException">
	/// The builder is complete, the lifetime is not Singleton, or the implementation is an open generic definition.
	/// </exception>
	/// <remarks>
	/// Eager registrations are resolved in registration order, with dependencies created as needed.
	/// Repeated calls have no additional effect. Existing instances are already created.
	/// This marker does not invoke lifecycle callbacks.
	/// </remarks>
	public RegistrationBuilder NonLazy()
	{
		EnsureMutable();
		if (_lifetime != Lifetime.Singleton)
			throw new InvalidOperationException("NonLazy is only supported for singleton registrations.");

		if (_implementationType.IsGenericTypeDefinition)
			throw new InvalidOperationException("An open generic registration cannot be NonLazy. Register a closed type instead.");

		_nonLazy = true;
		return this;
	}

	private void AddContract(Type contract)
	{
		if (_contracts.Contains(contract))
			throw new RegistrationException($"Contract {contract} is already exposed by this registration.");

		_contracts.Add(contract);
	}

	internal Registration Snapshot()
	{
		var activation = _factory is null && _instance is null
			? ConstructorActivation.ForType(_implementationType)
			: null;

		return new Registration(_implementationType,
			_lifetime,
			_contracts.Count == 0 ? [_implementationType] : _contracts.ToArray(),
			activation,
			_factory,
			_instance,
			_ownsInstance,
			_nonLazy);
	}

	internal void Complete()
	{
		_completed = true;
		_factory = null;
		_instance = null;
		_contracts.Clear();
	}

	/// <summary>Transfers responsibility for disposing a registered instance to the container.</summary>
	/// <returns>This registration builder for further configuration.</returns>
	/// <exception cref="InvalidOperationException">
	/// This is not an instance registration, or the owning builder has already built successfully.
	/// </exception>
	/// <remarks>
	/// Only instances supplied through RegisterInstance support this method.
	/// Ownership transfers before eager creation; if eager creation fails, cleanup disposes the instance.
	/// The container disposes the instance when it closes, even if it was never resolved.
	/// The caller must not dispose it or transfer ownership to another independent container.
	/// Non-disposable instances require no cleanup. Repeated calls before build have no additional effect.
	/// </remarks>
	public RegistrationBuilder OwnedByContainer()
	{
		EnsureMutable();
		if (_instance is null)
			throw new InvalidOperationException("OwnedByContainer is only supported for RegisterInstance registrations.");

		_ownsInstance = true;
		return this;
	}

	private void EnsureMutable()
	{
		if (_completed)
			throw new InvalidOperationException("Registration cannot be changed after Build().");
	}
}
