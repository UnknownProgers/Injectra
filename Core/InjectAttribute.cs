namespace Injectra;

/// <summary>Marks a method that an integration can use to inject dependencies into an existing object.</summary>
/// <remarks>The container does not invoke marked methods during registration, build, or resolution.</remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = true)]
public sealed class InjectAttribute : Attribute;
