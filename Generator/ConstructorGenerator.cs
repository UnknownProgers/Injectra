using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Injectra.Generator;

/// <summary>Emits direct constructors for statically visible Injectra registrations and closed resolutions.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class ConstructorGenerator : IIncrementalGenerator
{
	/// <inheritdoc />
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		// Return source strings rather than retaining symbols/compilations in incremental models.
		var constructors = context.SyntaxProvider.CreateSyntaxProvider(
				static (node, _) => node is InvocationExpressionSyntax invocation &&
					invocation.Expression is MemberAccessExpressionSyntax member &&
					member.Name.Identifier.ValueText is KnownNames.RegisterMethod or KnownNames.ResolveMethod,
				static (syntax, cancellationToken) => Describe(syntax, cancellationToken))
			.Where(static source => source is not null);

		context.RegisterSourceOutput(constructors.Collect(), static (output, sources) => Emit(output, sources));
	}

	private static string? Describe(GeneratorSyntaxContext context, CancellationToken cancellationToken)
	{
		var invocation = (InvocationExpressionSyntax)context.Node;
		if (context.SemanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method)
			return null;

		var core = method.ContainingAssembly;
		if (core.Name != KnownNames.CoreAssembly)
			return null;

		// Resolve canonical types from the method's assembly, even if the consumer declares shadowing types.
		var owner = method.ContainingType;
		var registration = method.Name == KnownNames.RegisterMethod &&
			SymbolEqualityComparer.Default.Equals(owner, core.GetTypeByMetadataName(KnownNames.ContainerBuilder));
		var resolution = method.Name == KnownNames.ResolveMethod &&
			(SymbolEqualityComparer.Default.Equals(owner, core.GetTypeByMetadataName(KnownNames.Container)) ||
				SymbolEqualityComparer.Default.Equals(owner, core.GetTypeByMetadataName(KnownNames.Resolver)));
		if (!registration && !resolution)
			return null;

		var compilation = context.SemanticModel.Compilation;

		ITypeSymbol? candidate = null;
		if (method.IsGenericMethod && (!registration || method.Parameters.Length == 1))
			candidate = method.TypeArguments[0];
		else if (!method.IsGenericMethod)
		{
			var typeOf = invocation.ArgumentList.Arguments
				.Select(argument => argument.Expression)
				.OfType<TypeOfExpressionSyntax>()
				.FirstOrDefault();
			if (typeOf is not null)
				candidate = context.SemanticModel.GetTypeInfo(typeOf.Type, cancellationToken).Type;
		}

		if (candidate is not INamedTypeSymbol type || !CanGenerate(type, compilation))
			return null;

		var constructors = type.InstanceConstructors.Where(ctor => ctor.DeclaredAccessibility == Accessibility.Public).ToArray();
		if (constructors.Length != 1)
			return null;

		var constructor = constructors[0];
		if (constructor.Parameters.Any(parameter => parameter.RefKind != RefKind.None || !CanName(parameter.Type, compilation)))
			return null;

		// A reflection fallback preserves existing behavior for constructors requiring object initializers or forbidden direct calls.
		var setsRequiredMembers = compilation.GetTypeByMetadataName(KnownNames.SetsRequiredMembersAttribute);
		if (HasRequiredMembers(type) && !HasAttribute(constructor, setsRequiredMembers))
			return null;
		var obsolete = compilation.GetTypeByMetadataName(KnownNames.ObsoleteAttribute);
		if (IsObsoleteError(type, obsolete) || IsObsoleteError(constructor, obsolete))
			return null;

		var name = Name(type);
		var parameters = constructor.Parameters;
		var dependencies = string.Join(",\n", parameters.Select(parameter => $"typeof({Name(parameter.Type)})"));
		var arguments = string.Join(",\n", parameters.Select((parameter, index) =>
			$"context.{KnownNames.ResolveArgument}<{Name(parameter.Type)}>({index})"));

		var dependencyArray = parameters.Length == 0
			? "global::System.Type.EmptyTypes"
			: $$"""
			    new global::System.Type[]
			    {
			    {{Indent(dependencies, 1)}}
			    }
			    """;
		var creation = parameters.Length == 0
			? $"new {name}()"
			: $$"""
			    new {{name}}(
			    {{Indent(arguments, 1)}})
			    """;
		var registrationArguments = string.Join(",\n",
			$"typeof({name})", dependencyArray, $"static context => {creation}");

		var registrationSource = $$"""
		                           global::{{KnownNames.GeneratedConstructors}}.{{KnownNames.RegisterGeneratedConstructor}}(
		                           {{Indent(registrationArguments, 1)}});
		                           """;
		return Indent(registrationSource, 2);
	}

	private static string Indent(string text, int depth)
	{
		var prefix = new string('\t', depth);
		return prefix + text.Replace("\n", "\n" + prefix);
	}

	private static bool CanGenerate(INamedTypeSymbol type, Compilation compilation) =>
		type.TypeKind == TypeKind.Class && !type.IsAbstract && !type.IsStatic &&
		SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly) && CanName(type, compilation);

	private static bool CanName(ITypeSymbol type, Compilation compilation)
	{
		if (type.TypeKind is TypeKind.TypeParameter or TypeKind.Error or TypeKind.Pointer or TypeKind.FunctionPointer || type.IsRefLikeType)
			return false;
		if (type is IArrayTypeSymbol array)
			return CanName(array.ElementType, compilation);
		if (type is not INamedTypeSymbol named)
			return false;
		return !named.IsUnboundGenericType && !named.IsFileLocal &&
			compilation.IsSymbolAccessibleWithin(named, compilation.Assembly) &&
			(named.ContainingType is null || CanName(named.ContainingType, compilation)) &&
			named.TypeArguments.All(argument => CanName(argument, compilation));
	}

	private static bool HasRequiredMembers(INamedTypeSymbol type)
	{
		for (var current = type; current is not null; current = current.BaseType)
			if (current.GetMembers().Any(member => member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true }))
				return true;
		return false;
	}

	private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol? attributeType) =>
		attributeType is not null && symbol.GetAttributes().Any(attribute =>
			SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType));

	private static bool IsObsoleteError(ISymbol symbol, INamedTypeSymbol? obsoleteAttribute) =>
		obsoleteAttribute is not null && symbol.GetAttributes().Any(attribute =>
			SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, obsoleteAttribute) &&
			attribute.ConstructorArguments.Length > 1 && attribute.ConstructorArguments[1].Value is true);

	private static string Name(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

	private static void Emit(SourceProductionContext context, ImmutableArray<string?> sources)
	{
		var registrations = sources
			.Where(source => source is not null)
			.Distinct(StringComparer.Ordinal)
			.OrderBy(source => source, StringComparer.Ordinal)
			.ToArray();
		if (registrations.Length == 0)
			return;

		var source = $$"""
		               // <auto-generated/>
		               #nullable enable
		               #pragma warning disable CS0612, CS0618
		               namespace Injectra.Generated;

		               internal static class ConstructorModule
		               {
		               	[global::{{KnownNames.ModuleInitializerAttribute}}]
		               	internal static void Initialize()
		               	{
		               {{string.Join("\n", registrations)}}
		               	}
		               }
		               """;

		// Raw templates may have CRLF after checkout on Windows; emitted sources always use LF and no BOM.
		source = source.Replace("\r\n", "\n") + "\n";
		context.AddSource("Injectra.Constructors.g.cs", SourceText.From(source, new UTF8Encoding(false)));
	}
}
