using System.Reflection;
using System.Runtime.ExceptionServices;
using Injectra.Internal.Resolution;

namespace Injectra.Internal.Activation;

internal sealed class ConstructorActivation(ConstructorInfo constructor, Binding[] dependencies) : IActivation
{
	private ConstructorInfo Constructor { get; } = constructor;
	public IReadOnlyList<Type> DependencyTypes { get; } = constructor
		.GetParameters()
		.Select(parameter => parameter.ParameterType)
		.ToArray();

	public IActivation WithDependencies(Binding[] bindings) => new ConstructorActivation(Constructor, bindings);

	public object Create(Container container)
	{
		var arguments = dependencies.Length == 0 ? [] : new object[dependencies.Length];
		for (var i = 0; i < dependencies.Length; i++)
			arguments[i] = container.ResolveBinding(dependencies[i]);
		try
		{
			return Constructor.Invoke(arguments);
		}
		catch (TargetInvocationException exception) when (exception.InnerException is not null)
		{
			ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
			throw;
		}
	}
}
