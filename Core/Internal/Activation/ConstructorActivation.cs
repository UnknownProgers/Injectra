using System.Reflection;
using Injectra.Internal.Resolution;

namespace Injectra.Internal.Activation;

internal sealed class ConstructorActivation(ConstructorInfo constructor, Binding[] dependencies, ConstructorInvoker? invoker = null) : IActivation
{
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
