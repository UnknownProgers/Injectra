using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Injectra.Generator;

internal static class GenericConstraints
{
	internal static IEnumerable<INamedTypeSymbol> Filter(Compilation compilation, IEnumerable<INamedTypeSymbol> types,
		CancellationToken cancellationToken)
	{
		var all = types.ToArray();
		var candidates = all.Where(type => type.IsGenericType)
			.OrderBy(ConstructorGenerator.Name, StringComparer.Ordinal).ToArray();
		if (candidates.Length == 0)
			return all;

		// Roslyn validates substituted constraints, including dependent, new(), unmanaged and notnull constraints.
		// This tree is used only for analysis; it is never emitted into the user's compilation.
		var checks = string.Join("\n", candidates.Select(type => $"_ = typeof({ConstructorGenerator.Name(type)});"));
		var source = $$"""
		               #nullable enable
		               namespace Injectra.Generated;
		               internal static class ConstraintProbe
		               {
		               	private static void Check()
		               	{
		               {{checks}}
		               	}
		               }
		               """;
		var options = (CSharpParseOptions?)compilation.SyntaxTrees.FirstOrDefault()?.Options;
		var tree = CSharpSyntaxTree.ParseText(source, options, cancellationToken: cancellationToken);
		var expressions = tree.GetRoot(cancellationToken).DescendantNodes().OfType<TypeOfExpressionSyntax>().ToArray();
		var diagnostics = compilation.AddSyntaxTrees(tree).GetSemanticModel(tree)
			.GetDiagnostics(cancellationToken: cancellationToken)
			.Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
			.ToArray();
		var valid = candidates.Where((_, index) => !diagnostics.Any(diagnostic =>
			diagnostic.Location.SourceTree == tree && diagnostic.Location.SourceSpan.IntersectsWith(expressions[index].Span)));
		return all.Where(type => !type.IsGenericType).Concat(valid);
	}
}
