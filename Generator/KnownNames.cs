namespace Injectra.Generator;

// Metadata names are the contract between the generator and Core; integration tests verify it against the real API.
internal static class KnownNames
{
	internal const string CoreAssembly = "Injectra.Core";
	internal const string ContainerBuilder = "Injectra.ContainerBuilder";
	internal const string RegistrationBuilder = "Injectra.RegistrationBuilder";
	internal const string AsMethod = "As";
	internal const string AsSelfMethod = "AsSelf";
	internal const string Container = "Injectra.Container";
	internal const string Resolver = "Injectra.IResolver";
	internal const string RegisterMethod = "Register";
	internal const string ResolveMethod = "Resolve";
	internal const string GeneratedConstructors = "Injectra.Generation.GeneratedConstructors";
	internal const string RegisterGeneratedConstructor = "Register";
	internal const string ResolveArgument = "Resolve";
	internal const string SetsRequiredMembersAttribute = "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute";
	internal const string ObsoleteAttribute = "System.ObsoleteAttribute";
	internal const string ModuleInitializerAttribute = "System.Runtime.CompilerServices.ModuleInitializer";
}
