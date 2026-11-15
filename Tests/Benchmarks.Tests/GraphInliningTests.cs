using Injectra.Benchmarks.Comparison;
using Injectra.Benchmarks.Experiments;
using Xunit;

namespace Injectra.Benchmarks.Tests;

public sealed class GraphInliningTests
{
	[Fact]
	public void FixedGraphHasEquivalentObjectsInAllMeasuredVariants()
	{
		var benchmarks = new GraphInliningBenchmarks();
		benchmarks.Setup();
		try
		{
			Func<Player>[] variants =
			[
				benchmarks.CurrentContainer, benchmarks.FactoryWithBoundaries, benchmarks.InlineMovement,
				benchmarks.InlineWithSharedSingleton, benchmarks.InlineWithPreboundSingleton,
				benchmarks.DirectConstructionLowerBound, benchmarks.Microsoft, benchmarks.Grove
			];
			foreach (var create in variants)
			{
				var first = create();
				var second = create();
				Assert.NotSame(first, second);
				Assert.NotSame(first.Movement, second.Movement);
				Assert.Same(first.Clock, first.Movement.Clock);
				Assert.Same(first.Clock, second.Clock);
			}
		}
		finally
		{
			benchmarks.Cleanup();
		}
	}

	[Fact]
	public void BoundaryFactoryHonorsMovementOverrides()
	{
		using var root = GraphInliningBenchmarks.CreateBoundaryFactory();
		var replacement = new Movement(new Clock());
		using var child = root.CreateChild(builder => builder.RegisterInstance(replacement));
		Assert.Same(replacement, child.Resolve<Player>().Movement);
	}

	[Fact]
	public void InlineMovementSkipsAnOverrideAndCannotBeUsedUnconditionally()
	{
		using var root = GraphInliningBenchmarks.CreateInlineFactory(shareSingleton: false);
		var replacement = new Movement(new Clock());
		using var child = root.CreateChild(builder => builder.RegisterInstance(replacement));
		Assert.NotSame(replacement, child.Resolve<Player>().Movement);
	}

	[Theory]
	[InlineData(false, 2)]
	[InlineData(true, 1)]
	public void SharingClockRequiresProvingItsSingletonLifetime(bool shareSingleton, int expectedCalls)
	{
		using var root = GraphInliningBenchmarks.CreateInlineFactory(shareSingleton);
		var calls = 0;
		using var child = root.CreateChild(builder => builder.Register<IClock>(_ =>
		{
			calls++;
			return new Clock();
		}, Lifetime.Transient));

		var player = child.Resolve<Player>();
		Assert.Equal(expectedCalls, calls);
		Assert.Equal(shareSingleton, ReferenceEquals(player.Clock, player.Movement.Clock));
	}

	[Fact]
	public void PreboundClockSkipsAChildOverrideAndRequiresRebinding()
	{
		using var root = GraphInliningBenchmarks.CreatePreboundFactory();
		var replacement = new Clock();
		using var child = root.CreateChild(builder => builder.RegisterInstance<IClock>(replacement));
		var player = child.Resolve<Player>();
		Assert.NotSame(replacement, player.Clock);
		Assert.Same(root.Resolve<IClock>(), player.Clock);
	}

	[Fact]
	public void BoundaryFactoryKeepsDynamicCycleDetection()
	{
		using var root = GraphInliningBenchmarks.CreateBoundaryFactory();
		using var child = root.CreateChild(builder =>
			builder.Register(resolver => resolver.Resolve<Player>().Movement, Lifetime.Transient));
		Assert.Throws<ResolutionException>(() => child.Resolve<Player>());
	}

	[Fact]
	public void InliningAnOverriddenFactoryWouldHideItsDynamicCycle()
	{
		using var root = GraphInliningBenchmarks.CreateInlineFactory(shareSingleton: false);
		using var child = root.CreateChild(builder =>
			builder.Register(resolver => resolver.Resolve<Player>().Movement, Lifetime.Transient));
		// Evidence that constructor-only inlining must be rejected when the actual binding is a factory.
		Assert.NotNull(child.Resolve<Player>());
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void MovementLifetimeMustBeCheckedBeforeInlining(bool inline)
	{
		using var root = inline
			? GraphInliningBenchmarks.CreateInlineFactory(shareSingleton: false)
			: GraphInliningBenchmarks.CreateBoundaryFactory();
		using var child = root.CreateChild(builder => builder.Register<Movement>(Lifetime.Singleton));
		var first = child.Resolve<Player>();
		var second = child.Resolve<Player>();
		Assert.Equal(!inline, ReferenceEquals(first.Movement, second.Movement));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void PreboundAncestorClockRequiresAnOwnerDisposalCheck(bool prebound)
	{
		using var root = prebound
			? GraphInliningBenchmarks.CreatePreboundFactory()
			: GraphInliningBenchmarks.CreateBoundaryFactory();
		var waiting = root.CreateChild();
		Exception? observed = null;
		root.CreateChild(builder => builder.RegisterInstance(new OnDispose(() =>
			observed = Record.Exception(() => waiting.Resolve<Player>()))).OwnedByContainer());

		root.Dispose();
		if (prebound)
			Assert.Null(observed); // The experimental shortcut skipped the ancestor's disposed state.
		else
			Assert.IsType<ObjectDisposedException>(observed);
	}

	[Fact]
	public void ContainerBackedVariantsRejectResolutionAfterDisposal()
	{
		var benchmarks = new GraphInliningBenchmarks();
		benchmarks.Setup();
		benchmarks.Cleanup();
		Assert.Throws<ObjectDisposedException>(() => benchmarks.CurrentContainer());
		Assert.Throws<ObjectDisposedException>(() => benchmarks.FactoryWithBoundaries());
		Assert.Throws<ObjectDisposedException>(() => benchmarks.InlineMovement());
		Assert.Throws<ObjectDisposedException>(() => benchmarks.InlineWithSharedSingleton());
		Assert.Throws<ObjectDisposedException>(() => benchmarks.InlineWithPreboundSingleton());
	}

	private sealed class OnDispose(Action action) : IDisposable
	{
		public void Dispose() => action();
	}
}
