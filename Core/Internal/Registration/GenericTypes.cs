namespace Injectra.Internal.Registration;

internal static class GenericTypes
{
	internal static void ValidateContract(Type implementation, Type contract)
	{
		if (!contract.IsGenericTypeDefinition || implementation.GetGenericArguments().Length != contract.GetGenericArguments().Length)
			throw new RegistrationException("Open generic contracts must have matching arity.");

		var arguments = implementation.GetGenericArguments();
		for (var type = implementation; type is not null; type = type.BaseType)
			if (Matches(type, contract, arguments))
				return;

		foreach (var type in implementation.GetInterfaces())
			if (Matches(type, contract, arguments))
				return;

		throw new RegistrationException("Open generic contracts must map implementation arguments in the same order.");
	}

	private static bool Matches(Type type, Type contract, Type[] arguments) =>
		type.IsGenericType && type.GetGenericTypeDefinition() == contract && type.GetGenericArguments().SequenceEqual(arguments);
}
