using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Injectra.Generator;

internal static class GenericConstructorDiscovery
{
	internal static IEnumerable<INamedTypeSymbol> Find(Compilation compilation,
		ImmutableArray<InvocationExpressionSyntax> calls, CancellationToken cancellationToken)
	{
		var roots = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
		var mappings = new List<(INamedTypeSymbol Implementation, INamedTypeSymbol Contract)>();
		var models = new Dictionary<SyntaxTree, SemanticModel>();
		foreach (var call in calls)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!models.TryGetValue(call.SyntaxTree, out var model))
				models.Add(call.SyntaxTree, model = compilation.GetSemanticModel(call.SyntaxTree));
			var candidate = ReadCandidate(call, model, cancellationToken);
			if (candidate is not { } hint)
				continue;

			if (hint.Registration && hint.Type.IsUnboundGenericType)
			{
				var implementation = hint.Type.OriginalDefinition;
				foreach (var contract in Contracts(call, model, implementation, cancellationToken))
					if (Matches(implementation, contract))
						mappings.Add((implementation, contract));
			}
			else if (ConstructorGenerator.CanName(hint.Type, compilation))
				roots.Add(hint.Type);
		}

		var discovered = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
		var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
		foreach (var root in roots)
			Visit(root, []);

		return GenericConstraints.Filter(compilation, discovered, cancellationToken);

		bool Visit(INamedTypeSymbol request, List<INamedTypeSymbol> path)
		{
			cancellationToken.ThrowIfCancellationRequested();
			// Stop a growing generic chain, while allowing finite chains that shrink or replace arguments.
			if (path.Any(ancestor => SymbolEqualityComparer.Default.Equals(ancestor.OriginalDefinition, request.OriginalDefinition) &&
				    Depth(request) > Depth(ancestor)))
				return false;
			if (!visited.Add(request))
				return true;

			var complete = true;
			path.Add(request);
			if (ConstructorGenerator.CanGenerate(request, compilation))
			{
				discovered.Add(request);
				var constructors = request.InstanceConstructors.Where(ctor => ctor.DeclaredAccessibility == Accessibility.Public).ToArray();
				if (constructors.Length == 1)
					foreach (var parameter in constructors[0].Parameters)
						if (parameter.Type is INamedTypeSymbol dependency && ConstructorGenerator.CanName(dependency, compilation))
							complete &= Visit(dependency, path);
			}

			if (request.IsGenericType)
				foreach (var mapping in mappings)
					if (SymbolEqualityComparer.Default.Equals(mapping.Contract, request.OriginalDefinition))
					{
						var closed = mapping.Implementation.Construct(request.TypeArguments.ToArray());
						if (ConstructorGenerator.CanName(closed, compilation))
							complete &= Visit(closed, path);
					}
			path.RemoveAt(path.Count - 1);
			if (!complete)
				visited.Remove(request);
			return complete;
		}
	}

	private static (INamedTypeSymbol Type, bool Registration)? ReadCandidate(InvocationExpressionSyntax call,
		SemanticModel model, CancellationToken cancellationToken)
	{
		if (model.GetSymbolInfo(call, cancellationToken).Symbol is not IMethodSymbol method)
			return null;
		var core = method.ContainingAssembly;
		if (core.Name != KnownNames.CoreAssembly)
			return null;

		var registration = method.Name == KnownNames.RegisterMethod &&
			SymbolEqualityComparer.Default.Equals(method.ContainingType, core.GetTypeByMetadataName(KnownNames.ContainerBuilder));
		var resolution = (method.Name == KnownNames.ResolveMethod || method.Name == KnownNames.ResolveAllMethod) &&
			(SymbolEqualityComparer.Default.Equals(method.ContainingType, core.GetTypeByMetadataName(KnownNames.Container)) ||
				SymbolEqualityComparer.Default.Equals(method.ContainingType, core.GetTypeByMetadataName(KnownNames.Resolver)));
		if (!registration && !resolution)
			return null;

		ITypeSymbol? type = null;
		if (method.IsGenericMethod && (!registration || method.Parameters.Length == 1))
			type = method.TypeArguments[0];
		else if (!method.IsGenericMethod)
			type = TypeOf(call, model, cancellationToken);
		return type is INamedTypeSymbol named ? (named, registration) : null;
	}

	private static IEnumerable<INamedTypeSymbol> Contracts(InvocationExpressionSyntax registration, SemanticModel model,
		INamedTypeSymbol implementation, CancellationToken cancellationToken)
	{
		var explicitContracts = false;
		var current = registration;
		while (current.Parent is MemberAccessExpressionSyntax member && member.Parent is InvocationExpressionSyntax next)
		{
			current = next;
			if (model.GetSymbolInfo(next, cancellationToken).Symbol is not IMethodSymbol method ||
			    method.ContainingAssembly.Name != KnownNames.CoreAssembly ||
			    !SymbolEqualityComparer.Default.Equals(method.ContainingType,
				    method.ContainingAssembly.GetTypeByMetadataName(KnownNames.RegistrationBuilder)))
				continue;

			if (method.Name == KnownNames.AsSelfMethod)
			{
				explicitContracts = true;
				yield return implementation;
			}
			else if (method.Name == KnownNames.AsImplementedInterfacesMethod && implementation.AllInterfaces.Length != 0)
			{
				explicitContracts = true;
				foreach (var contract in implementation.AllInterfaces)
					yield return contract.OriginalDefinition;
			}
			else if (method.Name == KnownNames.AsMethod)
			{
				explicitContracts = true;
				if (TypeOf(next, model, cancellationToken) is INamedTypeSymbol { IsUnboundGenericType: true } contract)
					yield return contract.OriginalDefinition;
			}
		}
		if (!explicitContracts)
			yield return implementation;
	}

	private static ITypeSymbol? TypeOf(InvocationExpressionSyntax call, SemanticModel model, CancellationToken cancellationToken)
	{
		var typeOf = call.ArgumentList.Arguments.Select(argument => argument.Expression).OfType<TypeOfExpressionSyntax>().FirstOrDefault();
		return typeOf is null ? null : model.GetTypeInfo(typeOf.Type, cancellationToken).Type;
	}

	private static bool Matches(INamedTypeSymbol implementation, INamedTypeSymbol contract)
	{
		if (implementation.Arity != contract.Arity || implementation.ContainingType is { IsGenericType: true })
			return false;
		for (var current = implementation; current is not null; current = current.BaseType)
			if (Positional(current))
				return true;
		return implementation.AllInterfaces.Any(Positional);

		bool Positional(INamedTypeSymbol type) => SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, contract) &&
			type.TypeArguments.SequenceEqual<ITypeSymbol>(implementation.TypeParameters, SymbolEqualityComparer.Default);
	}

	private static int Depth(ITypeSymbol type)
	{
		if (type is IArrayTypeSymbol array)
			return 1 + Depth(array.ElementType);
		if (type is not INamedTypeSymbol named)
			return 0;
		var arguments = named.TypeArguments.Length == 0 ? 0 : named.TypeArguments.Max(Depth);
		var outer = named.ContainingType is null ? 0 : Depth(named.ContainingType);
		return 1 + Math.Max(arguments, outer);
	}
}
