using Xunit;
using static Injectra.Generator.Tests.ConstructorGeneratorTests;

namespace Injectra.Generator.Tests;

public sealed class OpenGenericGeneratorTests
{
	[Fact]
	public void ImplementedInterfacesDiscoverClosedConstructor()
	{
		var result = Generate("""
		                      using Injectra;
		                      public interface IRepository<T>;
		                      public sealed class Repository<T> : IRepository<T>;
		                      public static class Entry
		                      {
		                      	public static bool Run()
		                      	{
		                      		var builder = new ContainerBuilder();
		                      		builder.Register(typeof(Repository<>), Lifetime.Singleton).AsImplementedInterfaces();
		                      		using var container = builder.Build();
		                      		return container.Resolve<IRepository<int>>() is Repository<int>;
		                      	}
		                      }
		                      """);
		Assert.Contains("new global::Repository<int>()", result.Source);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Fact]
	public void ResolveAllDiscoversEveryOpenGenericImplementation()
	{
		var result = Generate("""
		                      using Injectra;
		                      public interface IService<T>;
		                      public sealed class First<T> : IService<T>;
		                      public sealed class Second<T> : IService<T>;
		                      public static class Entry
		                      {
		                      	public static bool Run()
		                      	{
		                      		var builder = new ContainerBuilder();
		                      		builder.Register(typeof(First<>), Lifetime.Singleton).AsImplementedInterfaces();
		                      		builder.Register(typeof(Second<>), Lifetime.Singleton).AsImplementedInterfaces();
		                      		using var container = builder.Build();
		                      		return container.ResolveAll<IService<int>>().Count == 2;
		                      	}
		                      }
		                      """);
		Assert.Contains("new global::First<int>()", result.Source);
		Assert.Contains("new global::Second<int>()", result.Source);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Theory]
	[InlineData("Singleton", "true")]
	[InlineData("Transient", "false")]
	public void InterfaceClosuresRespectLifetimeAndAliases(string lifetime, string shared)
	{
		var result = Generate($$"""
		                        using Injectra;
		                        public interface IRepository<T>;
		                        public sealed class Repository<T> : IRepository<T>;
		                        public static class Entry
		                        {
		                        	public static bool Run()
		                        	{
		                        		var builder = new ContainerBuilder();
		                        		builder.Register(typeof(Repository<>), Lifetime.{{lifetime}}).As(typeof(IRepository<>)).AsSelf();
		                        		using var container = builder.Build();
		                        		var first = container.Resolve<IRepository<int>>();
		                        		var same = ReferenceEquals(first, container.Resolve<IRepository<int>>());
		                        		var alias = ReferenceEquals(first, container.Resolve<Repository<int>>());
		                        		return same == {{shared}} && alias == {{shared}} && container.Resolve<IRepository<string>>() is Repository<string>;
		                        	}
		                        }
		                        """);
		Assert.Contains("new global::Repository<int>()", result.Source);
		Assert.Contains("new global::Repository<string>()", result.Source);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Fact]
	public void ConstructorDependenciesDiscoverNestedGenericClosuresWithoutExplicitResolve()
	{
		var result = Generate("""
		                      using Injectra;
		                      using System.Collections.Generic;
		                      public interface IRepository<T>;
		                      public interface ICache<T>;
		                      public sealed class Cache<T> : ICache<T>;
		                      public sealed class Repository<T>(ICache<List<T>> cache) : IRepository<T>
		                      {
		                      	public ICache<List<T>> Cache { get; } = cache;
		                      }
		                      public sealed class Consumer(IRepository<int> repository)
		                      {
		                      	public IRepository<int> Repository { get; } = repository;
		                      }
		                      public static class Entry
		                      {
		                      	public static bool Run()
		                      	{
		                      		var builder = new ContainerBuilder();
		                      		builder.Register(typeof(Repository<>), Lifetime.Singleton).As(typeof(IRepository<>));
		                      		builder.Register(typeof(Cache<>), Lifetime.Singleton).As(typeof(ICache<>));
		                      		builder.Register<Consumer>(Lifetime.Transient);
		                      		using var container = builder.Build();
		                      		return container.Resolve<Consumer>().Repository is Repository<int> repository && repository.Cache is Cache<List<int>>;
		                      	}
		                      }
		                      """);
		Assert.Contains("new global::Repository<int>(", result.Source);
		Assert.Contains("new global::Cache<global::System.Collections.Generic.List<int>>()", result.Source);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Fact]
	public void InheritedInterfaceAndBaseContractsWithMultipleArgumentsAreSupported()
	{
		var result = Generate("""
		                      using Injectra;
		                      public interface IPair<TKey, TValue>;
		                      public class PairBase<TKey, TValue> : IPair<TKey, TValue>;
		                      public sealed class Pair<TKey, TValue> : PairBase<TKey, TValue>;
		                      public static class Entry
		                      {
		                      	public static bool Run()
		                      	{
		                      		var builder = new ContainerBuilder();
		                      		builder.Register(typeof(Pair<,>), Lifetime.Singleton).As(typeof(IPair<,>)).As(typeof(PairBase<,>));
		                      		using var container = builder.Build();
		                      		var pair = container.Resolve<IPair<string, int>>();
		                      		return pair is Pair<string, int> && ReferenceEquals(pair, container.Resolve<PairBase<string, int>>());
		                      	}
		                      }
		                      """);
		Assert.Contains("new global::Pair<string, int>()", result.Source);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Theory]
	[InlineData("class", "string", "int")]
	[InlineData("struct", "int", "string")]
	[InlineData("unmanaged", "int", "Managed")]
	[InlineData("new()", "Good", "NoDefault")]
	[InlineData("notnull", "int", "int?")]
	[InlineData("IEntity", "Good", "string")]
	public void ConstraintsRejectUnsafeClosuresWithoutBreakingTheUserCompilation(string constraint, string good, string bad)
	{
		var result = Generate($$"""
		                        using Injectra;
		                        public interface IEntity;
		                        public sealed class Good : IEntity;
		                        public sealed class NoDefault { private NoDefault() { } }
		                        public struct Managed { public string Value; }
		                        public interface IRepository<T>;
		                        public sealed class Repository<T> : IRepository<T> where T : {{constraint}};
		                        public static class Entry
		                        {
		                        	public static object Probe(IResolver resolver) => resolver.Resolve<IRepository<{{bad}}>>();
		                        	public static bool Run()
		                        	{
		                        		var builder = new ContainerBuilder();
		                        		builder.Register(typeof(Repository<>), Lifetime.Singleton).As(typeof(IRepository<>));
		                        		using var container = builder.Build();
		                        		return container.Resolve<IRepository<{{good}}>>() is Repository<{{good}}>;
		                        	}
		                        }
		                        """);
		Assert.Contains($"new global::Repository<{TypeName(good)}>()", result.Source);
		Assert.DoesNotContain($"new global::Repository<{TypeName(bad)}>", result.Source);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Fact]
	public void DependentAndRecursiveInterfaceConstraintsAreValidatedByTheCompiler()
	{
		var result = Generate("""
		                      using Injectra;
		                      public interface IEntity<T>;
		                      public class Base;
		                      public sealed class Entity : Base, IEntity<Entity>;
		                      public interface IRepository<T, TBase>;
		                      public sealed class Repository<T, TBase> : IRepository<T, TBase> where T : TBase, IEntity<T>;
		                      public static class Entry
		                      {
		                      	public static object Probe(IResolver resolver) => resolver.Resolve<IRepository<Base, Entity>>();
		                      	public static bool Run()
		                      	{
		                      		var builder = new ContainerBuilder();
		                      		builder.Register(typeof(Repository<,>), Lifetime.Singleton).As(typeof(IRepository<,>));
		                      		using var container = builder.Build();
		                      		return container.Resolve<IRepository<Entity, Base>>() is Repository<Entity, Base>;
		                      	}
		                      }
		                      """);
		Assert.Contains("new global::Repository<global::Entity, global::Base>()", result.Source);
		Assert.DoesNotContain("new global::Repository<global::Base, global::Entity>", result.Source);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Fact]
	public void ChildOpenOverridesAndClosedOverridesKeepTheirRuntimePrecedence()
	{
		var result = Generate("""
		                      using Injectra;
		                      public interface IRepository<T>;
		                      public sealed class Repository<T> : IRepository<T>;
		                      public sealed class Alternate<T> : IRepository<T>;
		                      public sealed class Special : IRepository<string>;
		                      public static class Entry
		                      {
		                      	public static bool Run()
		                      	{
		                      		var builder = new ContainerBuilder();
		                      		builder.Register(typeof(Repository<>), Lifetime.Singleton).As(typeof(IRepository<>));
		                      		builder.Register<Special>(Lifetime.Singleton).As<IRepository<string>>();
		                      		using var root = builder.Build();
		                      		using var child = root.CreateChild(b => b.Register(typeof(Alternate<>), Lifetime.Singleton).As(typeof(IRepository<>)));
		                      		return root.Resolve<IRepository<int>>() is Repository<int> && child.Resolve<IRepository<int>>() is Alternate<int>
		                      			&& root.Resolve<IRepository<string>>() is Special && child.Resolve<IRepository<string>>() is Alternate<string>;
		                      	}
		                      }
		                      """);
		Assert.Contains("new global::Repository<int>()", result.Source);
		Assert.Contains("new global::Alternate<int>()", result.Source);
		Assert.Contains("new global::Alternate<string>()", result.Source);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Fact]
	public void GenericTransientUsesChildDependencies()
	{
		var result = Generate("""
		                      using Injectra;
		                      public interface IClock { int Id { get; } }
		                      public sealed record Clock(int Id) : IClock;
		                      public interface IRepository<T> { IClock Clock { get; } }
		                      public sealed class Repository<T>(IClock clock) : IRepository<T> { public IClock Clock { get; } = clock; }
		                      public static class Entry
		                      {
		                      	public static bool Run()
		                      	{
		                      		var builder = new ContainerBuilder();
		                      		builder.RegisterInstance<IClock>(new Clock(1));
		                      		builder.Register(typeof(Repository<>), Lifetime.Transient).As(typeof(IRepository<>));
		                      		using var root = builder.Build();
		                      		using var child = root.CreateChild(b => b.RegisterInstance<IClock>(new Clock(2)));
		                      		return root.Resolve<IRepository<int>>().Clock.Id == 1 && child.Resolve<IRepository<int>>().Clock.Id == 2;
		                      	}
		                      }
		                      """);
		Assert.Contains("new global::Repository<int>(", result.Source);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Theory]
	[InlineData("IChain<System.Collections.Generic.List<T>>")]
	[InlineData("IChain<Outer<T>.Inner>")]
	public void ExpandingGraphsStopGenerationAndKeepRuntimeCycleProtection(string next)
	{
		var result = Generate($$"""
		                        using Injectra;
		                        public class Outer<T> { public sealed class Inner; }
		                        public interface IChain<T>;
		                        public sealed class Chain<T>({{next}} next) : IChain<T>;
		                        public static class Entry
		                        {
		                        	public static bool Run()
		                        	{
		                        		var builder = new ContainerBuilder();
		                        		builder.Register(typeof(Chain<>), Lifetime.Transient).As(typeof(IChain<>));
		                        		using var container = builder.Build();
		                        		try { container.Resolve<IChain<int>>(); return false; }
		                        		catch (RegistrationException) { return true; }
		                        	}
		                        }
		                        """);
		Assert.Equal(1, result.Source.Split("new global::Chain<").Length - 1);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Fact]
	public void ShrinkingGenericGraphsAreNotMistakenForInfiniteExpansion()
	{
		var result = Generate("""
		                      using Injectra;
		                      public sealed class Leaf;
		                      public interface IBox<T>;
		                      public sealed class Box<T>(T value) : IBox<T> { public T Value { get; } = value; }
		                      public static class Entry
		                      {
		                      	public static bool Run()
		                      	{
		                      		var leaf = new Leaf();
		                      		var builder = new ContainerBuilder();
		                      		builder.RegisterInstance(leaf);
		                      		builder.Register(typeof(Box<>), Lifetime.Transient).As(typeof(IBox<>)).AsSelf();
		                      		using var container = builder.Build();
		                      		return container.Resolve<IBox<Box<Leaf>>>() is Box<Box<Leaf>> box && ReferenceEquals(leaf, box.Value.Value);
		                      	}
		                      }
		                      """);
		Assert.Contains("new global::Box<global::Box<global::Leaf>>(", result.Source);
		Assert.Contains("new global::Box<global::Leaf>(", result.Source);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Fact]
	public void UnexposedInterfacesAreNotInferredFromImplementationShape()
	{
		var result = Generate("""
		                      using Injectra;
		                      public interface IRepository<T>;
		                      public interface IHidden<T>;
		                      public sealed class Repository<T> : IRepository<T>, IHidden<T>;
		                      public static class Entry
		                      {
		                      	public static bool Run()
		                      	{
		                      		var builder = new ContainerBuilder();
		                      		builder.Register(typeof(Repository<>), Lifetime.Singleton).As(typeof(IRepository<>));
		                      		using var container = builder.Build();
		                      		try { container.Resolve<IHidden<int>>(); return false; }
		                      		catch (ResolutionException) { return true; }
		                      	}
		                      }
		                      """);
		Assert.Empty(result.Source);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Fact]
	public void DynamicTypeContractsKeepTheReflectionFallback()
	{
		var result = Generate("""
		                      using Injectra;
		                      public interface IRepository<T>;
		                      public sealed class Repository<T> : IRepository<T>;
		                      public static class Entry
		                      {
		                      	public static bool Run()
		                      	{
		                      		var contract = typeof(IRepository<>);
		                      		var builder = new ContainerBuilder();
		                      		builder.Register(typeof(Repository<>), Lifetime.Singleton).As(contract);
		                      		using var container = builder.Build();
		                      		return container.Resolve<IRepository<int>>() is not null;
		                      	}
		                      }
		                      """);
		Assert.Empty(result.Source);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Fact]
	public void ReorderedGenericMappingRemainsARuntimeRegistrationError()
	{
		var result = Generate("""
		                      using Injectra;
		                      public interface IPair<TFirst, TSecond>;
		                      public sealed class Pair<TFirst, TSecond> : IPair<TSecond, TFirst>;
		                      public static class Entry
		                      {
		                      	public static object Probe(IResolver resolver) => resolver.Resolve<IPair<int, string>>();
		                      	public static bool Run()
		                      	{
		                      		var builder = new ContainerBuilder();
		                      		try { builder.Register(typeof(Pair<,>), Lifetime.Singleton).As(typeof(IPair<,>)); return false; }
		                      		catch (RegistrationException) { return true; }
		                      	}
		                      }
		                      """);
		Assert.Empty(result.Source);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Fact]
	public void PrivateGenericImplementationKeepsWorkingThroughFallback()
	{
		var result = Generate("""
		                      using Injectra;
		                      public interface IRepository<T>;
		                      public static class Entry
		                      {
		                      	private sealed class Repository<T> : IRepository<T> { public Repository() { } }
		                      	public static bool Run()
		                      	{
		                      		var builder = new ContainerBuilder();
		                      		builder.Register(typeof(Repository<>), Lifetime.Singleton).As(typeof(IRepository<>));
		                      		using var container = builder.Build();
		                      		return container.Resolve<IRepository<int>>() is Repository<int>;
		                      	}
		                      }
		                      """);
		Assert.Empty(result.Source);
		Assert.Equal(true, Run(result.Compilation));
	}

	[Fact]
	public void ExplicitRequestsAreNotLostAfterAnExpandingBranchWasPruned()
	{
		var result = Generate("""
		                      using Injectra;
		                      using System.Collections.Generic;
		                      public interface IChain<T>;
		                      public sealed class Chain<T>(IChain<List<T>> next) : IChain<T>;
		                      public static class Entry
		                      {
		                      	public static void Configure(ContainerBuilder builder)
		                      		=> builder.Register(typeof(Chain<>), Lifetime.Transient).As(typeof(IChain<>)).AsSelf();
		                      	public static void Probe(IResolver resolver)
		                      	{
		                      		resolver.Resolve<Chain<int>>();
		                      		resolver.Resolve<IChain<List<int>>>();
		                      	}
		                      }
		                      """);
		Assert.Contains("new global::Chain<int>(", result.Source);
		Assert.Contains("new global::Chain<global::System.Collections.Generic.List<int>>(", result.Source);
	}

	private static string TypeName(string type) => type is "int" or "string" or "int?" ? type : "global::" + type;
}
