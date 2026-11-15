using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Injectra.Generator.Tests;

public sealed class ConstructorGeneratorTests
{
	private static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
		.Split(Path.PathSeparator)
		.Append(typeof(Container).Assembly.Location)
		.Distinct(StringComparer.Ordinal)
		.Select(path => MetadataReference.CreateFromFile(path))
		.ToArray();

	[Fact]
	public void GeneratesDirectConstructorCallsAndRunsTheResult()
	{
		var result = Generate("""
		                      using Injectra;
		                      public sealed class Clock;
		                      public sealed class Service(Clock clock) { public Clock Clock { get; } = clock; }
		                      public static class Entry
		                      {
		                      	public static bool Run()
		                      	{
		                      		var builder = new ContainerBuilder();
		                      		builder.Register<Clock>(Lifetime.Singleton);
		                      		builder.Register<Service>(Lifetime.Transient);
		                      		using var container = builder.Build();
		                      		return ReferenceEquals(container.Resolve<Clock>(), container.Resolve<Service>().Clock);
		                      	}
		                      }
		                      """);

		Assert.Contains("new global::Service(", result.Source);
		Assert.Contains("context.Resolve<global::Clock>(0)", result.Source);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Theory]
	[InlineData("builder.Register<Service>(Lifetime.Transient);")]
	[InlineData("builder.Register(typeof(Service), Lifetime.Transient);")]
	[InlineData("builder.Register(typeof(Service), lifetime: Lifetime.Transient);")]
	[InlineData("builder.Register(lifetime: Lifetime.Transient, implementationType: typeof(Service));")]
	public void RecognizesTypedAndTypeOfRegistrations(string registration)
	{
		var result = Generate("""
		                      using Injectra;
		                      public sealed class Service;
		                      public static class Entry
		                      {
		                      	public static void Register(ContainerBuilder builder)
		                      	{
		                      		REGISTRATION
		                      	}
		                      }
		                      """.Replace("REGISTRATION", registration));
		Assert.Contains("new global::Service()", result.Source);
	}

	[Fact]
	public void RepeatedRegistrationsAreEmittedOnceAndOutputIsDeterministic()
	{
		const string source = """
		                      using Injectra;
		                      public sealed class Service;
		                      public static class Entry
		                      {
		                      	public static void Register(ContainerBuilder builder)
		                      	{
		                      		builder.Register<Service>(Lifetime.Singleton);
		                      		builder.Register<Service>(Lifetime.Transient);
		                      	}
		                      }
		                      """;
		var first = Generate(source).Source;
		Assert.Equal(first, Generate(source).Source);
		Assert.Equal(1, first.Split("new global::Service()").Length - 1);
	}

	[Fact]
	public void ClosedResolveOfOpenRegistrationGeneratesOnlyTheKnownClosure()
	{
		var result = Generate("""
		                      using Injectra;
		                      public sealed class Service<T>;
		                      public static class Entry
		                      {
		                      	public static bool Run()
		                      	{
		                      		var builder = new ContainerBuilder();
		                      		builder.Register(typeof(Service<>), Lifetime.Singleton);
		                      		using var container = builder.Build();
		                      		return ReferenceEquals(container.Resolve<Service<int>>(), container.Resolve<Service<int>>());
		                      	}
		                      }
		                      """);
		Assert.Contains("new global::Service<int>()", result.Source);
		Assert.DoesNotContain("typeof(global::Service<>)", result.Source);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Fact]
	public void FactoryAndInstanceRegistrationsDoNotGenerateConstructors()
	{
		var result = Generate("""
		                      using Injectra;
		                      public sealed class Service;
		                      public static class Entry
		                      {
		                      	public static void Register(ContainerBuilder builder)
		                      	{
		                      		builder.Register<Service>(_ => new Service(), Lifetime.Singleton);
		                      		builder.RegisterInstance(new Service());
		                      	}
		                      }
		                      """);
		Assert.Empty(result.Source);
	}

	[Fact]
	public void PrivateTypesAndGenericRegistrationMethodsKeepWorkingThroughFallback()
	{
		var result = Generate("""
		                      using Injectra;
		                      public static class Entry
		                      {
		                      	private sealed class Service;
		                      	private static void Register<T>(ContainerBuilder builder) where T : class
		                      		=> builder.Register<T>(Lifetime.Singleton);
		                      	public static bool Run()
		                      	{
		                      		var builder = new ContainerBuilder();
		                      		Register<Service>(builder);
		                      		using var container = builder.Build();
		                      		return container.Resolve<Service>() is not null;
		                      	}
		                      }
		                      """);
		Assert.Empty(result.Source);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Fact]
	public void KeywordIdentifiersAndManyArgumentsCompileAndRun()
	{
		var result = Generate("""
		                      using Injectra;
		                      namespace @event;
		                      public sealed class @class;
		                      public sealed class Service(@class a, @class b, @class c, @class d, @class e)
		                      {
		                      	public bool Shared { get; } = ReferenceEquals(a, e);
		                      }
		                      public static class Entry
		                      {
		                      	public static bool Run()
		                      	{
		                      		var builder = new ContainerBuilder();
		                      		builder.Register<@class>(Lifetime.Singleton);
		                      		builder.Register<Service>(Lifetime.Transient);
		                      		using var container = builder.Build();
		                      		return container.Resolve<Service>().Shared;
		                      	}
		                      }
		                      """);
		Assert.Contains("context.Resolve<global::@event.@class>(4)", result.Source);
		Assert.Equal(true, Run(result.Compilation, "event.Entry"));
	}

	[Fact]
	public void RequiredMembersWithoutConstructorAnnotationUseFallback()
	{
		var result = Generate("""
		                      using Injectra;
		                      public sealed class Service { public required string Name { get; init; } }
		                      public static class Entry
		                      {
		                      	public static bool Run()
		                      	{
		                      		var builder = new ContainerBuilder();
		                      		builder.Register<Service>(Lifetime.Singleton);
		                      		using var container = builder.Build();
		                      		return container.Resolve<Service>() is not null;
		                      	}
		                      }
		                      """);
		Assert.Empty(result.Source);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Fact]
	public void SimilarNamesFromOtherLibrariesAreIgnored()
	{
		var result = Generate("""
		                      public sealed class Service;
		                      public sealed class OtherBuilder { public void Register<T>() { } public T Resolve<T>() => default!; }
		                      public static class Entry { public static void Register(OtherBuilder b) { b.Register<Service>(); b.Resolve<Service>(); } }
		                      """);
		Assert.Empty(result.Source);
	}

	[Fact]
	public void InvalidConstructorRemainsARuntimeRegistrationError()
	{
		var result = Generate("""
		                      using Injectra;
		                      public sealed class Service { public Service() { } public Service(string value) { } }
		                      public static class Entry
		                      {
		                      	public static bool Run()
		                      	{
		                      		var builder = new ContainerBuilder();
		                      		builder.Register<Service>(Lifetime.Singleton);
		                      		try { builder.Build(); return false; } catch (RegistrationException) { return true; }
		                      	}
		                      }
		                      """);
		Assert.Empty(result.Source);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Fact]
	public void AliasedBuilderAndInterfaceResolverAreRecognized()
	{
		var result = Generate("""
		                      using Builder = Injectra.ContainerBuilder;
		                      using Resolver = Injectra.IResolver;
		                      public sealed class Registered;
		                      public sealed class Resolved;
		                      public static class Entry
		                      {
		                      	public static void Configure(Builder builder) => builder.Register<Registered>(Injectra.Lifetime.Singleton);
		                      	public static Resolved Get(Resolver resolver) => resolver.Resolve<Resolved>();
		                      }
		                      """);
		Assert.Contains("new global::Registered()", result.Source);
		Assert.Contains("new global::Resolved()", result.Source);
	}

	[Fact]
	public void ShadowingTypesDoNotHideTheActualCoreSymbols()
	{
		var result = Generate("""
		                      extern alias actual;
		                      namespace Injectra
		                      {
		                      	public sealed class ContainerBuilder { public void Register<T>() { } }
		                      	public interface IResolver { T Resolve<T>(); }
		                      	public sealed class Container : IResolver { public T Resolve<T>() => default!; }
		                      }
		                      public sealed class Registered;
		                      public sealed class Resolved;
		                      public sealed class Ignored;
		                      public static class Entry
		                      {
		                      	public static void Configure(actual::Injectra.ContainerBuilder real, Injectra.ContainerBuilder fake)
		                      	{
		                      		real.Register<Registered>(actual::Injectra.Lifetime.Singleton);
		                      		fake.Register<Ignored>();
		                      	}
		                      	public static void Get(actual::Injectra.Container real, actual::Injectra.IResolver resolver, Injectra.Container fake)
		                      	{
		                      		real.Resolve<Registered>();
		                      		resolver.Resolve<Resolved>();
		                      		fake.Resolve<Ignored>();
		                      	}
		                      }
		                      """, aliasCore: true);
		Assert.Contains("new global::Registered()", result.Source);
		Assert.Contains("new global::Resolved()", result.Source);
		Assert.DoesNotContain("global::Ignored", result.Source);
	}

	[Fact]
	public void SetsRequiredMembersAttributeIsRecognizedBySymbol()
	{
		var result = Generate("""
		                      using Injectra;
		                      using System.Diagnostics.CodeAnalysis;
		                      public sealed class Service
		                      {
		                      	public required string Name { get; init; }
		                      	[SetsRequiredMembers]
		                      	public Service() => Name = "ready";
		                      }
		                      public static class Entry
		                      {
		                      	public static bool Run()
		                      	{
		                      		var builder = new ContainerBuilder();
		                      		builder.Register<Service>(Lifetime.Singleton);
		                      		using var container = builder.Build();
		                      		return container.Resolve<Service>().Name == "ready";
		                      	}
		                      }
		                      """);
		Assert.Contains("new global::Service()", result.Source);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Fact]
	public void ObsoleteErrorConstructorUsesFallbackWithoutEmittingAForbiddenCall()
	{
		var result = Generate("""
		                      using Injectra;
		                      public sealed class Service
		                      {
		                      	[System.Obsolete("Do not call directly", true)]
		                      	public Service() { }
		                      }
		                      public static class Entry
		                      {
		                      	public static bool Run()
		                      	{
		                      		var builder = new ContainerBuilder();
		                      		builder.Register<Service>(Lifetime.Singleton);
		                      		using var container = builder.Build();
		                      		return container.Resolve<Service>() is not null;
		                      	}
		                      }
		                      """);
		Assert.Empty(result.Source);
		Assert.Equal(true, Run(result.Compilation));
	}

	private static (Compilation Compilation, string Source) Generate(string source, bool aliasCore = false)
	{
		var syntax = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
		var references = References.Select(reference => aliasCore && reference.Display == typeof(Container).Assembly.Location
			? reference.WithProperties(reference.Properties.WithAliases(["global", "actual"]))
			: reference);
		var compilation = CSharpCompilation.Create("GeneratorTest_" + Guid.NewGuid().ToString("N"), [syntax], references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, usings: ["System"]));
		GeneratorDriver driver = CSharpGeneratorDriver.Create([new ConstructorGenerator().AsSourceGenerator()], parseOptions: (CSharpParseOptions)syntax.Options);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var diagnostics);
		Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
		Assert.Empty(updated.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
		var trees = driver.GetRunResult().GeneratedTrees;
		foreach (var tree in trees)
		{
			Assert.DoesNotContain("\r", tree.ToString());
			Assert.Empty(tree.GetText().Encoding!.GetPreamble());
		}
		return (updated, string.Join("\n", trees.Select(tree => tree.ToString())));
	}

	private static object? Run(Compilation compilation, string entry = "Entry")
	{
		using var stream = new MemoryStream();
		var result = compilation.Emit(stream);
		Assert.True(result.Success, string.Join("\n", result.Diagnostics));
		var assembly = Assembly.Load(stream.ToArray());
		return assembly.GetType(entry)!.GetMethod("Run")!.Invoke(null, null);
	}
}
