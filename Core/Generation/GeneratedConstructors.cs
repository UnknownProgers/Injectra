using System.ComponentModel;
using Injectra.Internal.Activation;

namespace Injectra.Generation;

/// <summary>Registers constructor metadata and direct calls emitted by Injectra's generator.</summary>
/// <remarks>Generated factories do not capture containers or resolved services. Registration occurs during module initialization.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedConstructors
{
	private static readonly Dictionary<Type, IActivation> Constructors = [];

	/// <summary>Registers a generated constructor. A duplicate implementation keeps its existing descriptor.</summary>
	/// <param name="implementationType">The closed implementation type.</param>
	/// <param name="dependencyTypes">Constructor parameter types in declaration order.</param>
	/// <param name="factory">A direct, non-null constructor call using the supplied activation context.</param>
	/// <exception cref="ArgumentNullException">Any argument is null.</exception>
	public static void Register(Type implementationType, Type[] dependencyTypes, Func<ActivationContext, object> factory)
	{
		ArgumentNullException.ThrowIfNull(implementationType);
		ArgumentNullException.ThrowIfNull(dependencyTypes);
		ArgumentNullException.ThrowIfNull(factory);
		Constructors.TryAdd(implementationType, new GeneratedActivation(dependencyTypes.ToArray(), factory, []));
	}

	internal static IActivation? Find(Type implementationType) => Constructors.GetValueOrDefault(implementationType);
}
