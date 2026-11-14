using Xunit;

namespace Injectra.Core.Tests.Resolution;

public sealed class ContainerTests
{
	[Theory]
	[InlineData(Lifetime.Singleton, true)]
	[InlineData(Lifetime.Transient, false)]
	public void LifetimeControlsIdentity(Lifetime lifetime, bool shared)
	{
		var builder = new ContainerBuilder();
		builder.Register<Weapon>(lifetime);
		var container = builder.Build();
		Assert.Equal(shared, ReferenceEquals(container.Resolve<Weapon>(), container.Resolve<Weapon>()));
	}

	[Fact]
	public void AliasesShareOneSingletonAndRequireExplicitSelf()
	{
		var builder = new ContainerBuilder();
		builder.Register<Weapon>(Lifetime.Singleton)
			.As<IWeapon>()
			.As<ISecondary>();

		var container = builder.Build();
		Assert.Same(container.Resolve<IWeapon>(), container.Resolve<ISecondary>());
		Assert.Throws<ResolutionException>(() => container.Resolve<Weapon>());
	}

	[Fact]
	public void ConstructorGraphUsesExposedContracts()
	{
		var builder = new ContainerBuilder();
		builder.Register<Weapon>(Lifetime.Singleton)
			.As<IWeapon>()
			.AsSelf();

		builder.Register<Player>(Lifetime.Transient);
		var container = builder.Build();
		Assert.Same(container.Resolve<Weapon>(), container.Resolve<Player>().Weapon);
	}

	[Fact]
	public void InstanceCanExposeAnInterface()
	{
		var weapon = new Weapon();
		var builder = new ContainerBuilder();
		builder.RegisterInstance(weapon).As<IWeapon>();
		Assert.Same(weapon, builder.Build().Resolve<IWeapon>());
	}

	[Theory]
	[InlineData(Lifetime.Singleton, 1)]
	[InlineData(Lifetime.Transient, 2)]
	public void FactoryUsesResolverAndRespectsLifetime(Lifetime lifetime, int expectedCalls)
	{
		var calls = 0;
		var builder = new ContainerBuilder();
		builder.Register<Weapon>(Lifetime.Singleton).As<IWeapon>();
		builder.Register<Player>(resolver =>
		{
			calls++;
			return new Player(resolver.Resolve<IWeapon>());
		}, lifetime);
		var container = builder.Build();
		Assert.Same(container.Resolve<IWeapon>(), container.Resolve<Player>().Weapon);
		container.Resolve<Player>();
		Assert.Equal(expectedCalls, calls);
	}

	[Fact]
	public void SuccessfulBuildPreventsReuseAndFluentChanges()
	{
		var builder = new ContainerBuilder();
		var registration = builder.Register<Weapon>(Lifetime.Singleton);
		var container = builder.Build();
		Assert.Throws<InvalidOperationException>(() => builder.Build());
		Assert.Throws<InvalidOperationException>(() => builder.Register<Player>(Lifetime.Transient));
		Assert.Throws<InvalidOperationException>(() => builder.RegisterInstance(new Weapon()));
		Assert.Throws<InvalidOperationException>(() => builder.Register<Weapon>(_ => new Weapon(), Lifetime.Singleton));
		Assert.Throws<InvalidOperationException>(() => registration.As<IWeapon>());
		Assert.Throws<InvalidOperationException>(() => registration.AsSelf());
		Assert.NotNull(container.Resolve<Weapon>());
	}

	[Fact]
	public void FailedBuildAllowsConfigurationToBeCorrected()
	{
		var builder = new ContainerBuilder();
		builder.Register<Player>(Lifetime.Transient);
		Assert.Throws<RegistrationException>(() => builder.Build());
		builder.Register<Weapon>(Lifetime.Singleton).As<IWeapon>();
		Assert.NotNull(builder.Build().Resolve<Player>());
	}

	[Fact]
	public void DuplicateContractsAreRejected()
	{
		var builder = new ContainerBuilder();
		builder.Register<Weapon>(Lifetime.Singleton).As<IWeapon>();
		builder.Register<Weapon>(Lifetime.Transient).As<IWeapon>();
		Assert.Throws<RegistrationException>(() => builder.Build());
	}

	[Fact]
	public void InvalidAliasAndRepeatedAliasAreRejected()
	{
		var registration = new ContainerBuilder().Register<Weapon>(Lifetime.Singleton);
		Assert.Throws<RegistrationException>(() => registration.As<IDisposable>());
		registration.As<IWeapon>();
		Assert.Throws<RegistrationException>(() => registration.As<IWeapon>());
	}

	[Fact]
	public void MissingDependencyIncludesFullPath()
	{
		var builder = new ContainerBuilder();
		builder.Register<Player>(Lifetime.Transient);
		var exception = Assert.Throws<RegistrationException>(() => builder.Build());
		Assert.Contains("Player -> IWeapon", exception.Message);
	}

	[Fact]
	public void ConstructorCycleIsRejectedDuringBuild()
	{
		var builder = new ContainerBuilder();
		builder.Register<CycleA>(Lifetime.Singleton);
		builder.Register<CycleB>(Lifetime.Singleton);
		var exception = Assert.Throws<RegistrationException>(() => builder.Build());
		Assert.Contains("CycleA -> CycleB -> CycleA", exception.Message);
	}

	[Fact]
	public void FactoryCycleThroughAliasIsDetected()
	{
		var builder = new ContainerBuilder();
		builder.Register<Weapon>(resolver => (Weapon)resolver.Resolve<IWeapon>(), Lifetime.Singleton)
			.AsSelf().As<IWeapon>();
		Assert.Throws<ResolutionException>(() => builder.Build().Resolve<Weapon>());
	}

	[Fact]
	public void FailedFactoryIsRetriedAndNullIsRejected()
	{
		var calls = 0;
		var builder = new ContainerBuilder();
		builder.Register<Weapon>(_ => ++calls == 1 ? null! : new Weapon(), Lifetime.Singleton);
		var container = builder.Build();
		Assert.Throws<ResolutionException>(() => container.Resolve<Weapon>());
		Assert.NotNull(container.Resolve<Weapon>());
		Assert.Equal(2, calls);
	}

	[Fact]
	public void ConstructorExceptionIsPreserved()
	{
		var builder = new ContainerBuilder();
		builder.Register<ThrowsOnCreation>(Lifetime.Transient);
		Assert.Throws<ApplicationException>(() => builder.Build().Resolve<ThrowsOnCreation>());
	}

	[Fact]
	public void InvalidConstructorsAndAbstractTypesAreRejected()
	{
		var builder = new ContainerBuilder();
		builder.Register<Ambiguous>(Lifetime.Transient);
		Assert.Throws<RegistrationException>(() => builder.Build());
		builder = new ContainerBuilder();
		builder.Register<IWeapon>(Lifetime.Singleton);
		Assert.Throws<RegistrationException>(() => builder.Build());
	}

	[Fact]
	public void NullInputsAndUnknownLifetimeAreRejected()
	{
		var builder = new ContainerBuilder();
		Assert.Throws<ArgumentNullException>(() => builder.RegisterInstance<Weapon>(null!));
		Assert.Throws<ArgumentNullException>(() => builder.Register<Weapon>(null!, Lifetime.Singleton));
		Assert.Throws<ArgumentOutOfRangeException>(() => builder.Register<Weapon>((Lifetime)99));
		Assert.Throws<ArgumentNullException>(() => builder.Build().Resolve(null!));
	}

	[Fact]
	public void ResolverInjectionUsesTheOwningContainer()
	{
		var builder = new ContainerBuilder();
		builder.Register<NeedsResolver>(Lifetime.Transient);
		var first = builder.Build();
		var secondBuilder = new ContainerBuilder();
		secondBuilder.Register<NeedsResolver>(Lifetime.Transient);
		var second = secondBuilder.Build();
		Assert.Same(first, first.Resolve<IResolver>());
		Assert.Same(first, first.Resolve<NeedsResolver>().Resolver);
		Assert.Same(second, second.Resolve<NeedsResolver>().Resolver);
	}

	[Fact]
	public void ResolverContractCannotBeReplaced()
	{
		var builder = new ContainerBuilder();
		builder.RegisterInstance<IResolver>(new ContainerBuilder().Build());
		Assert.Throws<RegistrationException>(() => builder.Build());
	}

	[Fact]
	public void BuildDoesNotExecuteUserCode()
	{
		var builder = new ContainerBuilder();
		builder.Register<ThrowsOnCreation>(Lifetime.Singleton);
		builder.Register<Weapon>(_ => throw new ApplicationException(), Lifetime.Singleton);
		Assert.NotNull(builder.Build());
	}

	[Fact]
	public void DynamicFactoryDependencyIsValidatedAtResolve()
	{
		var builder = new ContainerBuilder();
		builder.Register<Player>(resolver => new Player(resolver.Resolve<IWeapon>()), Lifetime.Transient);
		var container = builder.Build();
		var exception = Assert.Throws<ResolutionException>(() => container.Resolve<Player>());
		Assert.Contains("Player -> IWeapon", exception.Message);
	}

	[Fact]
	public void FailedActivationClearsPathAndDoesNotCacheFailure()
	{
		var calls = 0;
		var builder = new ContainerBuilder();
		builder.Register<Weapon>(_ => ++calls == 1
			? throw new ApplicationException("First attempt")
			: new Weapon(), Lifetime.Singleton).As<IWeapon>();
		builder.Register<Player>(Lifetime.Singleton);
		var container = builder.Build();
		Assert.Throws<ApplicationException>(() => container.Resolve<Player>());
		var player = container.Resolve<Player>();
		Assert.Same(player, container.Resolve<Player>());
		Assert.Same(player.Weapon, container.Resolve<IWeapon>());
		Assert.Equal(2, calls);
	}

	public sealed class NeedsResolver(IResolver resolver)
	{
		public IResolver Resolver { get; } = resolver;
	}

	public interface IWeapon;
	public interface ISecondary;
	public sealed class Weapon : IWeapon, ISecondary;
	public sealed class Player(IWeapon weapon)
	{
		public IWeapon Weapon { get; } = weapon;
	}
	public sealed class CycleA(CycleB b)
	{
		public CycleB B { get; } = b;
	}
	public sealed class CycleB(CycleA a)
	{
		public CycleA A { get; } = a;
	}
	public sealed class Ambiguous
	{
		public Ambiguous()
		{
		}

		public Ambiguous(IWeapon weapon)
		{
		}
	}
	public sealed class ThrowsOnCreation
	{
		public ThrowsOnCreation() => throw new ApplicationException("Constructor failed.");
	}
}
