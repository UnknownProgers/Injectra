using Injectra.Diagnostics;
using Xunit;

namespace Injectra.Core.Tests.Diagnostics;

public sealed class ContainerDiagnosticsTests
{
	[Fact]
	public void ContainerNamesAreOptionalAndNotInherited()
	{
		var builder = new ContainerBuilder("Application");
		using var root = builder.Build();
		using var child = root.CreateChild(name: "Level");
		using var unnamed = child.CreateChild();
		var snapshot = root.GetDiagnostics();

		Assert.Equal("Application", root.Name);
		Assert.Equal("Level", child.Name);
		Assert.Null(unnamed.Name);
		Assert.Equal("Application", snapshot.Name);
		Assert.Equal("Level", Assert.Single(snapshot.Children).Name);
		Assert.Null(Assert.Single(snapshot.Children[0].Children).Name);
		Assert.Contains("Container #1 (Application)", snapshot.ToString());
		Assert.Contains("Container #2 (Level)", snapshot.ToString());
		Assert.Contains("└── Container #3" + Environment.NewLine, snapshot.ToString());
	}

	[Fact]
	public void NamedChildSupportsRegistrationAndPreparation()
	{
		using var root = new ContainerBuilder().Build();
		string? nameDuringPreparation = null;
		using var child = root.CreateChild(local => local.Register<Clock>(Lifetime.Singleton),
			prepared => nameDuringPreparation = prepared.Name, name: "Level");
		using var unnamed = root.CreateChild(name: null);

		Assert.Equal("Level", nameDuringPreparation);
		Assert.NotNull(child.Resolve<Clock>());
		Assert.Null(unnamed.Name);
	}

	[Fact]
	public void ExistingBuilderAndSnapshotConstructorsKeepNullNames()
	{
		var builder = new ContainerBuilder();
		using var container = builder.Build();
		var snapshot = new ContainerSnapshot(1, null, [], []);
		var (id, parent, registrations, children) = snapshot;

		Assert.Null(container.Name);
		Assert.Null(container.GetDiagnostics().Name);
		Assert.Null(snapshot.Name);
		Assert.Equal(1, id);
		Assert.Null(parent);
		Assert.Empty(registrations);
		Assert.Empty(children);
		Assert.Equal("Container #1" + Environment.NewLine, snapshot.ToString());
	}

	[Fact]
	public void CaptureDoesNotCreateServicesOrInvokeFactories()
	{
		var factoryCalls = 0;
		var builder = new ContainerBuilder();
		builder.Register<Clock>(Lifetime.Singleton).As<IClock>();
		builder.Register<Worker>(Lifetime.Transient);
		builder.Register<FactoryService>(_ => { factoryCalls++; return new FactoryService(); }, Lifetime.Singleton);
		using var container = builder.Build();

		var snapshot = container.GetDiagnostics();
		var text = snapshot.ToString();

		Assert.Equal(0, factoryCalls);
		Assert.All(snapshot.Registrations, registration => Assert.False(registration.IsCreated));
		Assert.Contains("Dependencies: unknown (factory)", text);
		var worker = Assert.Single(snapshot.Registrations, entry => entry.ImplementationType == typeof(Worker));
		var dependency = Assert.Single(worker.Dependencies);
		Assert.Equal(typeof(IClock), dependency.ContractType);
		Assert.Equal(typeof(Clock), dependency.ImplementationType);
	}

	[Fact]
	public void CreatedEntriesComeFirstThenSortByImplementationName()
	{
		var builder = new ContainerBuilder();
		builder.Register<Clock>(Lifetime.Singleton);
		builder.Register<FactoryService>(Lifetime.Singleton);
		builder.RegisterInstance(new ExternalService());
		using var container = builder.Build();
		container.Resolve<FactoryService>();

		var entries = container.GetDiagnostics().Registrations;

		Assert.Equal([typeof(ExternalService), typeof(FactoryService), typeof(Clock)], entries.Select(entry => entry.ImplementationType));
		Assert.True(entries[0].IsCreated);
		Assert.True(entries[1].IsCreated);
		Assert.False(entries[2].IsCreated);
		Assert.Equal(ActivationKind.Instance, entries[0].Activation);
		Assert.Equal(ActivationKind.Constructor, entries[1].Activation);
	}

	[Fact]
	public void AliasesAreGroupedAndOwnershipIsReported()
	{
		var builder = new ContainerBuilder();
		builder.Register<Clock>(Lifetime.Singleton).As<IClock>().AsSelf();
		builder.RegisterInstance(new ExternalService());
		builder.RegisterInstance(new OwnedService()).OwnedByContainer();
		builder.Register<FactoryService>(Lifetime.Transient);
		using var container = builder.Build();
		container.Resolve<Clock>();
		container.Resolve<FactoryService>();

		var snapshot = container.GetDiagnostics();
		var clock = Assert.Single(snapshot.Registrations, entry => entry.ImplementationType == typeof(Clock));
		Assert.Equal(2, clock.Contracts.Count);
		Assert.Equal(InstanceOwnership.Container, clock.Ownership);
		Assert.Equal(snapshot.Id, clock.OwnerContainerId);
		Assert.Equal(InstanceOwnership.External, Assert.Single(snapshot.Registrations, entry => entry.ImplementationType == typeof(ExternalService)).Ownership);
		Assert.Equal(InstanceOwnership.Container, Assert.Single(snapshot.Registrations, entry => entry.ImplementationType == typeof(OwnedService)).Ownership);
		var transient = Assert.Single(snapshot.Registrations, entry => entry.Lifetime == Lifetime.Transient);
		Assert.False(transient.IsCreated);
		Assert.Equal(InstanceOwnership.Consumer, transient.Ownership);
	}

	[Fact]
	public void ChildrenOnlyListLocalRegistrationsAndMarkOverrides()
	{
		var builder = new ContainerBuilder();
		builder.Register<Clock>(Lifetime.Singleton).As<IClock>();
		builder.Register<FactoryService>(Lifetime.Singleton);
		using var root = builder.Build();
		using var child = root.CreateChild(local =>
		{
			local.Register<OtherClock>(Lifetime.Singleton).As<IClock>();
			local.Register<Worker>(Lifetime.Transient);
		});

		var snapshot = root.GetDiagnostics();
		var childSnapshot = Assert.Single(snapshot.Children);
		Assert.Equal(snapshot.Id, childSnapshot.ParentId);
		Assert.Equal(2, childSnapshot.Registrations.Count);
		var clock = Assert.Single(childSnapshot.Registrations, entry => entry.ImplementationType == typeof(OtherClock));
		Assert.Equal([typeof(IClock)], clock.OverriddenContracts);
		var worker = Assert.Single(childSnapshot.Registrations, entry => entry.ImplementationType == typeof(Worker));
		Assert.Equal(childSnapshot.Id, Assert.Single(worker.Dependencies).DeclaringContainerId);
		Assert.Contains("Override: IClock", snapshot.ToString());
	}

	[Fact]
	public void InheritedTransientDependenciesUseChildOverridesButSingletonsKeepOwnerContext()
	{
		var builder = new ContainerBuilder();
		builder.Register<Clock>(Lifetime.Singleton).As<IClock>();
		builder.Register<Worker>(Lifetime.Transient);
		builder.Register<SingletonWorker>(Lifetime.Singleton);
		using var root = builder.Build();
		using var child = root.CreateChild(local =>
		{
			local.Register<OtherClock>(Lifetime.Singleton).As<IClock>();
			local.Register<Consumer>(Lifetime.Transient);
		});

		var snapshot = child.GetDiagnostics();
		var consumer = Assert.Single(snapshot.Registrations, entry => entry.ImplementationType == typeof(Consumer));
		var transient = Assert.Single(consumer.Dependencies, entry => entry.ContractType == typeof(Worker));
		var singleton = Assert.Single(consumer.Dependencies, entry => entry.ContractType == typeof(SingletonWorker));
		Assert.Equal(typeof(OtherClock), Assert.Single(transient.Dependencies).ImplementationType);
		Assert.Equal(typeof(Clock), Assert.Single(singleton.Dependencies).ImplementationType);
		Assert.Equal(snapshot.ParentId, singleton.DeclaringContainerId);
	}

	[Fact]
	public void RepeatedDependenciesAreReferencesRatherThanExpandedAgain()
	{
		var builder = new ContainerBuilder();
		builder.Register<Clock>(Lifetime.Singleton).As<IClock>();
		builder.Register<Worker>(Lifetime.Transient);
		builder.Register<SingletonWorker>(Lifetime.Singleton);
		builder.Register<Consumer>(Lifetime.Transient);
		using var container = builder.Build();

		var consumer = Assert.Single(container.GetDiagnostics().Registrations, entry => entry.ImplementationType == typeof(Consumer));
		var references = consumer.Dependencies.SelectMany(entry => entry.Dependencies).ToArray();
		Assert.False(references[0].IsReference);
		Assert.True(references[1].IsReference);
	}

	[Fact]
	public void SnapshotsRemainUsableAfterResolutionAndDisposal()
	{
		var builder = new ContainerBuilder();
		builder.Register<Clock>(Lifetime.Singleton);
		var container = builder.Build();
		var before = container.GetDiagnostics();
		var originalText = before.ToString();
		container.Resolve<Clock>();
		var after = container.GetDiagnostics();
		container.Dispose();

		Assert.False(Assert.Single(before.Registrations).IsCreated);
		Assert.True(Assert.Single(after.Registrations).IsCreated);
		Assert.Equal(originalText, before.ToString());
		Assert.Throws<ObjectDisposedException>(() => container.GetDiagnostics());
	}

	[Fact]
	public void ClosedGenericSingletonsAreVisibleWithoutCreatingAdditionalSpecializations()
	{
		var builder = new ContainerBuilder();
		builder.Register(typeof(Repository<>), Lifetime.Singleton).As(typeof(IRepository<>));
		using var container = builder.Build();
		Assert.False(Assert.Single(container.GetDiagnostics().Registrations).IsSpecialization);
		container.Resolve<IRepository<int>>();

		var entries = container.GetDiagnostics().Registrations;

		Assert.Equal(2, entries.Count);
		var closed = Assert.Single(entries, entry => entry.IsSpecialization);
		Assert.True(closed.IsCreated);
		Assert.Equal(typeof(Repository<int>), closed.ImplementationType);
		Assert.Equal([typeof(IRepository<int>)], closed.Contracts);
	}

	[Fact]
	public void DisposedChildrenDisappearFromNewSnapshots()
	{
		using var root = new ContainerBuilder().Build();
		var child = root.CreateChild();
		Assert.Single(root.GetDiagnostics().Children);
		child.Dispose();
		Assert.Empty(root.GetDiagnostics().Children);
	}

	[Fact]
	public void TextTreeHasDeterministicBranchesAndExplicitFactoryBoundaries()
	{
		var builder = new ContainerBuilder();
		builder.RegisterInstance(new Clock()).As<IClock>();
		builder.Register<Worker>(Lifetime.Transient);
		using var root = builder.Build();
		using var child = root.CreateChild(local => local.Register<FactoryService>(_ => new FactoryService(), Lifetime.Singleton));

		var text = root.GetDiagnostics().ToString();

		var expected = string.Join(Environment.NewLine,
			"Container #1",
			"├── Container #2",
			"│   └── ○ FactoryService · Singleton · Factory · Registered · Owned: Container #2",
			"│       ├── Contracts: FactoryService",
			"│       └── Dependencies: unknown (factory)",
			"├── ● Clock · Singleton · Instance · Created · External",
			"│   └── Contracts: IClock",
			"└── ○ Worker · Transient · Constructor · Registered · Consumer",
			"    ├── Contracts: Worker",
			"    └── IClock → Clock · Local", "");
		Assert.Equal(expected, text);
	}

	[Fact]
	public void TextSortsDetachedRegistrationsByStateThenImplementationName()
	{
		var builder = new ContainerBuilder();
		builder.Register<Clock>(Lifetime.Singleton);
		builder.Register<FactoryService>(Lifetime.Singleton);
		builder.RegisterInstance(new ExternalService());
		using var container = builder.Build();
		container.Resolve<FactoryService>();
		var snapshot = container.GetDiagnostics();
		var text = (snapshot with { Registrations = snapshot.Registrations.Reverse().ToArray() }).ToString();

		Assert.True(text.IndexOf("● ExternalService", StringComparison.Ordinal) < text.IndexOf("● FactoryService", StringComparison.Ordinal));
		Assert.True(text.IndexOf("● FactoryService", StringComparison.Ordinal) < text.IndexOf("○ Clock", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void TextGroupsGenericContractsWithOrWithoutOpenDefinition(bool openDefinition)
	{
		var builder = new ContainerBuilder();
		if (openDefinition)
			builder.Register(typeof(Repository<>), Lifetime.Singleton).As(typeof(IRepository<>));
		else
			builder.Register<Repository<int>>(Lifetime.Singleton).As<IRepository<int>>();
		using var container = builder.Build();
		container.Resolve<IRepository<int>>();

		var text = container.GetDiagnostics().ToString();
		Assert.Equal(1, text.Split(Environment.NewLine).Count(line => line.EndsWith("── IRepository<>", StringComparison.Ordinal)));
		Assert.Contains("● Repository<Int32>", text);
		Assert.Contains("Contracts: IRepository<Int32>", text);
		if (openDefinition)
		{
			Assert.Contains("○ Repository<>", text);
			Assert.Contains("Dependencies: deferred until generic closure", text);
			Assert.True(text.IndexOf("● Repository<Int32>", StringComparison.Ordinal) < text.IndexOf("○ Repository<>", StringComparison.Ordinal));
		}
	}

	[Fact]
	public void GenericTextGroupPreservesMultipleImplementationsAndTheirContracts()
	{
		var builder = new ContainerBuilder();
		builder.Register(typeof(Repository<>), Lifetime.Singleton).As(typeof(IRepository<>));
		builder.Register(typeof(OtherRepository<>), Lifetime.Singleton).As(typeof(IRepository<>));
		using var container = builder.Build();
		container.ResolveAll<IRepository<int>>();

		var text = container.GetDiagnostics().ToString();
		Assert.Contains("○ Repository<>", text);
		Assert.Contains("○ OtherRepository<>", text);
		Assert.Contains("● Repository<Int32>", text);
		Assert.Contains("● OtherRepository<Int32>", text);
		Assert.Equal(2, text.Split(Environment.NewLine).Count(line => line.Contains("Contracts: IRepository<Int32>", StringComparison.Ordinal)));
	}

	[Fact]
	public void ClosedGenericDependenciesUseAlreadyPreparedSpecializations()
	{
		var builder = new ContainerBuilder();
		builder.Register(typeof(Repository<>), Lifetime.Singleton).As(typeof(IRepository<>));
		builder.Register<RepositoryConsumer>(Lifetime.Transient);
		using var container = builder.Build();

		var snapshot = container.GetDiagnostics();
		var consumer = Assert.Single(snapshot.Registrations, entry => entry.ImplementationType == typeof(RepositoryConsumer));
		Assert.Equal(typeof(Repository<int>), Assert.Single(consumer.Dependencies).ImplementationType);
		Assert.All(snapshot.Registrations, entry => Assert.False(entry.IsCreated));
	}

	[Fact]
	public void FactoryReturningExternalInstanceDoesNotReportContainerOwnership()
	{
		var instance = new ExternalService();
		var builder = new ContainerBuilder();
		builder.RegisterInstance(instance);
		builder.Register<object>(_ => instance, Lifetime.Singleton);
		using var container = builder.Build();
		container.Resolve<object>();

		var factory = Assert.Single(container.GetDiagnostics().Registrations, entry => entry.Activation == ActivationKind.Factory);
		Assert.True(factory.IsCreated);
		Assert.Equal(InstanceOwnership.External, factory.Ownership);
		Assert.Null(factory.OwnerContainerId);
	}

	[Fact]
	public void SnapshotDoesNotKeepDisposedServicesAlive()
	{
		var (snapshot, service) = CaptureReleasedService();

		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();

		Assert.False(service.IsAlive);
		Assert.True(Assert.Single(snapshot.Registrations).IsCreated);
		GC.KeepAlive(snapshot);
	}

	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	private static (ContainerSnapshot Snapshot, WeakReference Service) CaptureReleasedService()
	{
		var builder = new ContainerBuilder();
		builder.Register<Clock>(Lifetime.Singleton);
		using var container = builder.Build();
		var instance = container.Resolve<Clock>();
		return (container.GetDiagnostics(), new WeakReference(instance));
	}

	[Fact]
	public void ResolverDependenciesReferToTheRequestingContainer()
	{
		var builder = new ContainerBuilder();
		builder.Register<ResolverConsumer>(Lifetime.Transient);
		using var container = builder.Build();

		var snapshot = container.GetDiagnostics();
		var dependency = Assert.Single(Assert.Single(snapshot.Registrations).Dependencies);
		Assert.Equal(typeof(IResolver), dependency.ContractType);
		Assert.Equal(snapshot.Id, dependency.DeclaringContainerId);
		Assert.Equal(ActivationKind.Instance, dependency.Activation);
	}

	public sealed class ResolverConsumer(IResolver resolver)
	{
		public IResolver Resolver { get; } = resolver;
	}

	public sealed class RepositoryConsumer(IRepository<int> repository)
	{
		public IRepository<int> Repository { get; } = repository;
	}

	public interface IClock;
	public sealed class Clock : IClock;
	public sealed class OtherClock : IClock;
	public sealed class Worker(IClock clock) { public IClock Clock { get; } = clock; }
	public sealed class SingletonWorker(IClock clock) { public IClock Clock { get; } = clock; }
	public sealed class Consumer(Worker worker, SingletonWorker singleton)
	{
		public Worker Worker { get; } = worker;
		public SingletonWorker Singleton { get; } = singleton;
	}
	public sealed class FactoryService;
	public sealed class ExternalService;
	public sealed class OwnedService;
	public interface IRepository<T>;
	public sealed class Repository<T> : IRepository<T>;
	public sealed class OtherRepository<T> : IRepository<T>;
}
