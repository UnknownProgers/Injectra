using System.Reflection;
using Injectra.Internal.Registration;

namespace Injectra;

/// <summary>Selects the service contracts exposed by a single registration.</summary>
/// <typeparam name="T">The registered implementation or instance type.</typeparam>
/// <remarks>Without explicit contracts, the registration exposes <typeparamref name="T"/> itself.</remarks>
public sealed class RegistrationBuilder<T> : IRegistrationBuilder where T : class
{
	private readonly List<Type> _contracts = [];
	private readonly Lifetime _lifetime;
	private Func<IResolver, object>? _factory;
	private T? _instance;
	private bool _completed;

	internal RegistrationBuilder(Lifetime lifetime, Func<IResolver, object>? factory = null, T? instance = null)
	{
		_lifetime = lifetime;
		_factory = factory;
		_instance = instance;
	}

	/// <summary>Adds a service contract to this registration.</summary>
	/// <typeparam name="TContract">A type assignable from <typeparamref name="T"/>.</typeparam>
	/// <returns>This registration builder for further configuration.</returns>
	/// <exception cref="RegistrationException">
	/// <typeparamref name="T"/> is not assignable to <typeparamref name="TContract"/>, or the contract was already added.
	/// </exception>
	/// <exception cref="InvalidOperationException">The owning container builder has already built successfully.</exception>
	/// <remarks>Adding an explicit contract removes the implicit self contract. Use <see cref="AsSelf"/> to expose it too.</remarks>
	public RegistrationBuilder<T> As<TContract>()
	{
		EnsureMutable();
		var contract = typeof(TContract);
		if (!contract.IsAssignableFrom(typeof(T)))
			throw new RegistrationException($"{typeof(T)} does not implement {contract}.");

		AddContract(contract);
		return this;
	}

	/// <summary>Explicitly exposes <typeparamref name="T"/> alongside the other contracts of this registration.</summary>
	/// <returns>This registration builder for further configuration.</returns>
	/// <exception cref="RegistrationException">The self contract was already explicitly added.</exception>
	/// <exception cref="InvalidOperationException">The owning container builder has already built successfully.</exception>
	public RegistrationBuilder<T> AsSelf()
	{
		EnsureMutable();
		AddContract(typeof(T));
		return this;
	}

	private void AddContract(Type contract)
	{
		if (_contracts.Contains(contract))
			throw new RegistrationException($"Contract {contract} is already exposed by this registration.");

		_contracts.Add(contract);
	}

	Registration IRegistrationBuilder.Snapshot()
	{
		ConstructorInfo? constructor = null;
		if (_factory is null && _instance is null)
		{
			var type = typeof(T);
			if (type.IsAbstract || type.IsInterface || type.ContainsGenericParameters)
				throw new RegistrationException($"{type} cannot be constructed. Register an implementation, instance or factory.");

			var constructors = type.GetConstructors();
			if (constructors.Length != 1)
				throw new RegistrationException($"{type} must have exactly one public constructor.");

			constructor = constructors[0];
		}

		return new Registration(typeof(T),
			_lifetime,
			_contracts.Count == 0 ? [typeof(T)] : _contracts.ToArray(),
			constructor,
			_factory,
			_instance);
	}

	void IRegistrationBuilder.Complete()
	{
		_completed = true;
		_factory = null;
		_instance = null;
		_contracts.Clear();
	}

	private void EnsureMutable()
	{
		if (_completed)
			throw new InvalidOperationException("Registration cannot be changed after Build().");
	}
}
