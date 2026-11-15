using Xunit;

namespace Injectra.Core.Tests.Resolution;

public sealed class OpenGenericTests
{
	[Theory]
	[InlineData(Lifetime.Singleton, true)]
	[InlineData(Lifetime.Transient, false)]
	public void ClosedServicesRespectLifetime(Lifetime lifetime, bool shared)
	{
		var builder = new ContainerBuilder();
		builder.Register(typeof(Service<>), lifetime).As(typeof(IService<>));
		using var container = builder.Build();
		var first = container.Resolve<IService<int>>();
		Assert.IsType<Service<int>>(first);
		Assert.Equal(shared, ReferenceEquals(first, container.Resolve<IService<int>>()));
		Assert.NotSame(first, container.Resolve<IService<string>>());
	}

	[Fact]
	public void AliasesAndSelfShareClosedSingleton()
	{
		var builder = new ContainerBuilder();
		builder.Register(typeof(Service<>), Lifetime.Singleton).As(typeof(IService<>)).AsSelf();
		using var container = builder.Build();
		Assert.Same(container.Resolve<IService<int>>(), container.Resolve<Service<int>>());
	}

	[Fact]
	public void ClosedOverrideWinsOverOpenRegistration()
	{
		var special = new Service<int>();
		var builder = new ContainerBuilder();
		builder.Register(typeof(Service<>), Lifetime.Singleton).As(typeof(IService<>));
		builder.RegisterInstance<IService<int>>(special);
		using var container = builder.Build();
		Assert.Same(special, container.Resolve<IService<int>>());
		Assert.IsType<Service<string>>(container.Resolve<IService<string>>());
	}

	[Fact]
	public void GenericConstructorDependenciesAreResolved()
	{
		var builder = new ContainerBuilder();
		builder.Register(typeof(Service<>), Lifetime.Singleton).As(typeof(IService<>));
		builder.Register(typeof(Consumer<>), Lifetime.Transient);
		using var container = builder.Build();
		Assert.Same(container.Resolve<IService<int>>(), container.Resolve<Consumer<int>>().Service);
	}

	[Fact]
	public void ChildOpenOverrideWinsOverParentClosedContract()
	{
		var builder = new ContainerBuilder();
		builder.RegisterInstance<IService<int>>(new Service<int>());
		using var root = builder.Build();
		using var child = root.CreateChild(b => b.Register(typeof(Alternate<>), Lifetime.Singleton).As(typeof(IService<>)));
		Assert.IsType<Alternate<int>>(child.Resolve<IService<int>>());
		Assert.IsType<Service<int>>(root.Resolve<IService<int>>());
	}

	[Fact]
	public void ParentGenericSingletonUsesParentDependencies()
	{
		var builder = new ContainerBuilder();
		builder.RegisterInstance<IService<int>>(new Service<int>());
		builder.Register(typeof(Consumer<>), Lifetime.Singleton);
		using var root = builder.Build();
		using var child = root.CreateChild(b => b.RegisterInstance<IService<int>>(new Alternate<int>()));
		Assert.Same(root.Resolve<Consumer<int>>(), child.Resolve<Consumer<int>>());
		Assert.IsType<Service<int>>(child.Resolve<Consumer<int>>().Service);
	}

	[Fact]
	public void ParentGenericTransientUsesChildDependencies()
	{
		var builder = new ContainerBuilder();
		builder.RegisterInstance<IService<int>>(new Service<int>());
		builder.Register(typeof(Consumer<>), Lifetime.Transient);
		using var root = builder.Build();
		using var child = root.CreateChild(b => b.RegisterInstance<IService<int>>(new Alternate<int>()));
		Assert.IsType<Alternate<int>>(child.Resolve<Consumer<int>>().Service);
		Assert.IsType<Service<int>>(root.Resolve<Consumer<int>>().Service);
	}

	[Fact]
	public void ConstraintFailureDoesNotPoisonValidClosures()
	{
		var builder = new ContainerBuilder();
		builder.Register(typeof(ReferenceService<>), Lifetime.Singleton).As(typeof(IService<>));
		using var container = builder.Build();
		Assert.Throws<ResolutionException>(() => container.Resolve<IService<int>>());
		Assert.NotNull(container.Resolve<IService<object>>());
	}

	[Fact]
	public void GenericCyclesFailRepeatedlyWithoutCachingBrokenPlans()
	{
		var builder = new ContainerBuilder();
		builder.Register(typeof(Cycle<>), Lifetime.Singleton);
		using var container = builder.Build();
		for (var i = 0; i < 2; i++)
			Assert.Throws<RegistrationException>(() => container.Resolve<Cycle<int>>());
	}

	[Fact]
	public void MissingClosedDependencyIsValidatedDuringBuild()
	{
		var builder = new ContainerBuilder();
		builder.Register(typeof(Consumer<>), Lifetime.Singleton);
		builder.Register<UsesConsumer>(Lifetime.Singleton);
		Assert.Throws<RegistrationException>(() => builder.Build());
	}

	[Fact]
	public void GenericSingletonsAreDisposedOncePerClosedType()
	{
		var builder = new ContainerBuilder();
		builder.Register(typeof(Service<>), Lifetime.Singleton).As(typeof(IService<>)).AsSelf();
		var container = builder.Build();
		var first = container.Resolve<Service<int>>();
		var second = container.Resolve<Service<string>>();
		container.Resolve<IService<int>>();
		container.Dispose();
		Assert.Equal(1, first.Disposals);
		Assert.Equal(1, second.Disposals);
		Assert.Throws<ObjectDisposedException>(() => container.Resolve<Service<int>>());
	}

	[Fact]
	public void InvalidOpenMappingsAreRejected()
	{
		var registration = new ContainerBuilder().Register(typeof(Service<>), Lifetime.Singleton);
		Assert.Throws<RegistrationException>(() => registration.As(typeof(IDictionary<,>)));
		Assert.Throws<RegistrationException>(() => registration.As(typeof(IComparable<>)));
		Assert.Throws<RegistrationException>(() => registration.As(typeof(IService<int>)));
		Assert.Throws<ArgumentNullException>(() => registration.As(null!));
		registration.As(typeof(IService<>));
		Assert.Throws<RegistrationException>(() => registration.As(typeof(IService<>)));
	}

	[Fact]
	public void ReorderedMappingsAreExplicitlyUnsupported()
	{
		var registration = new ContainerBuilder().Register(typeof(Reordered<,>), Lifetime.Transient);
		Assert.Throws<RegistrationException>(() => registration.As(typeof(IPair<,>)));
	}

	[Fact]
	public void OpenDefinitionCannotBeResolvedAndBuilderIsSingleUse()
	{
		var builder = new ContainerBuilder();
		var registration = builder.Register(typeof(Service<>), Lifetime.Singleton);
		using var container = builder.Build();
		Assert.Throws<ResolutionException>(() => container.Resolve(typeof(Service<>)));
		Assert.Throws<InvalidOperationException>(() => registration.AsSelf());
		Assert.Throws<InvalidOperationException>(() => builder.Register(typeof(Service<>), Lifetime.Singleton));
	}

	[Fact]
	public void ExpandingGenericGraphFailsWithoutOverflowingStack()
	{
		var builder = new ContainerBuilder();
		builder.Register(typeof(Expanding<>), Lifetime.Transient);
		using var container = builder.Build();
		for (var i = 0; i < 2; i++)
			Assert.Throws<RegistrationException>(() => container.Resolve<Expanding<int>>());
	}

	[Fact]
	public void MultipleArgumentsAndInheritedContractsAreSupported()
	{
		var builder = new ContainerBuilder();
		builder.Register(typeof(Pair<,>), Lifetime.Singleton).As(typeof(IPair<,>)).AsSelf();
		using var container = builder.Build();
		Assert.Same(container.Resolve<Pair<int, string>>(), container.Resolve<IPair<int, string>>());
	}

	[Fact]
	public void ChildDisposalDoesNotDisposeParentGenericSingleton()
	{
		var builder = new ContainerBuilder();
		builder.Register(typeof(Service<>), Lifetime.Singleton).As(typeof(IService<>)).AsSelf();
		using var root = builder.Build();
		var child = root.CreateChild();
		var service = child.Resolve<Service<int>>();
		child.Dispose();
		Assert.Equal(0, service.Disposals);
		Assert.Same(service, root.Resolve<IService<int>>());
		root.Dispose();
		Assert.Equal(1, service.Disposals);
	}

	[Fact]
	public void NonGenericTypeRegistrationUsesExistingValidation()
	{
		var builder = new ContainerBuilder();
		Assert.Throws<ArgumentNullException>(() => builder.Register(null!, Lifetime.Transient));
		Assert.Throws<ArgumentOutOfRangeException>(() => builder.Register(typeof(Service<>), (Lifetime)99));
		builder.Register(typeof(Service<int>), Lifetime.Singleton).As<IService<int>>();
		using var container = builder.Build();
		Assert.IsType<Service<int>>(container.Resolve<IService<int>>());
	}

	public interface IService<T>;
	public sealed class Service<T> : IService<T>, IDisposable
	{
		public int Disposals { get; private set; }
		public void Dispose() => Disposals++;
	}
	public sealed class Alternate<T> : IService<T>;
	public sealed class ReferenceService<T> : IService<T> where T : class;
	public sealed class Consumer<T>(IService<T> service)
	{
		public IService<T> Service { get; } = service;
	}
	public sealed class Cycle<T>(Cycle<T> dependency)
	{
		public Cycle<T> Dependency { get; } = dependency;
	}
	public sealed class UsesConsumer(Consumer<int> consumer)
	{
		public Consumer<int> Consumer { get; } = consumer;
	}
	public sealed class Expanding<T>(Expanding<List<T>> dependency)
	{
		public Expanding<List<T>> Dependency { get; } = dependency;
	}
	public class PairBase<TFirst, TSecond> : IPair<TFirst, TSecond>;
	public sealed class Pair<TFirst, TSecond> : PairBase<TFirst, TSecond>;
	public interface IPair<TFirst, TSecond>;
	public sealed class Reordered<TFirst, TSecond> : IPair<TSecond, TFirst>;
}
