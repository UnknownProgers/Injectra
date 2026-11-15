using System.Reflection;
using Injectra.Generation;
using Injectra.Internal.Resolution;

namespace Injectra.Internal.Activation;

internal sealed class ConstructorActivation(ConstructorInfo constructor, Binding[] dependencies, ConstructorInvoker? invoker = null) : IActivation
{
	internal static IActivation ForType(Type type)
	{
		if (!type.IsClass || type.IsAbstract || (type.ContainsGenericParameters && !type.IsGenericTypeDefinition))
			throw new RegistrationException($"{type} cannot be constructed. Register an implementation, instance or factory.");

		if (GeneratedConstructors.Find(type) is { } generated)
			return generated;

		var constructors = type.GetConstructors();
		if (constructors.Length != 1)
			throw new RegistrationException($"{type} must have exactly one public constructor.");

		return new ConstructorActivation(constructors[0], []);
	}

	private ConstructorInfo Constructor { get; } = constructor;
	public IReadOnlyList<Type> DependencyTypes { get; } = constructor
		.GetParameters()
		.Select(parameter => parameter.ParameterType)
		.ToArray();

	public IActivation WithDependencies(Binding[] bindings) => new ConstructorActivation(Constructor, bindings, invoker ?? ConstructorInvoker.Create(Constructor));

	public object Create(Container container)
	{
		object Argument(int index) => container.ResolveBinding(dependencies[index]);

		return dependencies.Length switch
		{
			0 => invoker!.Invoke(),
			1 => invoker!.Invoke(Argument(0)),
			2 => invoker!.Invoke(Argument(0), Argument(1)),
			3 => invoker!.Invoke(Argument(0), Argument(1), Argument(2)),
			4 => invoker!.Invoke(Argument(0), Argument(1), Argument(2), Argument(3)),
			_ => CreateMany(container)
		};
	}

	private object CreateMany(Container container)
	{
		var arguments = new object?[dependencies.Length];
		for (var i = 0; i < dependencies.Length; i++)
			arguments[i] = container.ResolveBinding(dependencies[i]);

		return invoker!.Invoke(arguments.AsSpan());
	}
}
