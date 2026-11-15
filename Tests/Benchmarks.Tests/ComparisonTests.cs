using Injectra.Benchmarks.Comparison;
using Xunit;

namespace Injectra.Benchmarks.Tests;

public sealed class ComparisonTests
{
	[Fact]
	public void WarmScenariosHaveEquivalentLifetimesAndGraphs()
	{
		var benchmarks = new WarmComparisonBenchmarks();
		benchmarks.Setup();
		try
		{
			AssertScenario(benchmarks.InjectraSingleton, benchmarks.InjectraTransient, benchmarks.InjectraGraph);
			AssertScenario(benchmarks.MicrosoftSingleton, benchmarks.MicrosoftTransient, benchmarks.MicrosoftGraph);
			AssertScenario(benchmarks.GroveSingleton, benchmarks.GroveTransient, benchmarks.GroveGraph);
			Assert.NotSame(benchmarks.InjectraSingleton(), benchmarks.MicrosoftSingleton());
			Assert.NotSame(benchmarks.InjectraSingleton(), benchmarks.GroveSingleton());
		}
		finally
		{
			benchmarks.Cleanup();
		}
	}

	[Fact]
	public void FirstResolveUsesFreshSingletonsForEachSample()
	{
		var benchmarks = new FirstResolveComparisonBenchmarks();
		Player[] Sample()
		{
			benchmarks.Setup();
			try
			{
				return [benchmarks.Injectra(), benchmarks.Microsoft(), benchmarks.Grove()];
			}
			finally
			{
				benchmarks.Cleanup();
			}
		}
		var first = Sample();
		var second = Sample();
		for (var i = 0; i < first.Length; i++)
		{
			Assert.Same(first[i].Clock, first[i].Movement.Clock);
			Assert.NotSame(first[i].Clock, second[i].Clock);
		}
	}

	[Fact]
	public void OpenGenericScenariosShareRepositoryButCreateNewConsumers()
	{
		var benchmarks = new OpenGenericComparisonBenchmarks();
		benchmarks.Setup();
		try
		{
			Assert.Same(benchmarks.InjectraSingleton(), benchmarks.InjectraGraph().Repository);
			Assert.Same(benchmarks.MicrosoftSingleton(), benchmarks.MicrosoftGraph().Repository);
			Assert.NotSame(benchmarks.InjectraGraph(), benchmarks.InjectraGraph());
			Assert.NotSame(benchmarks.MicrosoftGraph(), benchmarks.MicrosoftGraph());
		}
		finally
		{
			benchmarks.Cleanup();
		}
	}

	[Fact]
	public void FirstOpenGenericResolveClosesFreshGraphsForEachSample()
	{
		var benchmarks = new FirstOpenGenericComparisonBenchmarks();
		GenericConsumer<int>[] Sample()
		{
			benchmarks.Setup();
			try
			{
				return [benchmarks.Injectra(), benchmarks.Microsoft()];
			}
			finally
			{
				benchmarks.Cleanup();
			}
		}
		var first = Sample();
		var second = Sample();
		for (var i = 0; i < first.Length; i++)
			Assert.NotSame(first[i].Repository, second[i].Repository);
	}

	[Fact]
	public void GrovePublishedApiRejectsOpenContractMapping()
	{
		var builder = new GroveGames.DependencyInjection.ContainerBuilder();
#pragma warning disable GGDI002 // Intentionally invalid registration: verify the published runtime limitation.
		Assert.Throws<ArgumentException>(() => builder.AddSingleton(typeof(IRepository<>), typeof(Repository<>)));
#pragma warning restore GGDI002
	}

	private static void AssertScenario(Func<IClock> singleton, Func<Enemy> transient, Func<Player> graph)
	{
		Assert.IsType<Clock>(singleton());
		Assert.Same(singleton(), singleton());
		Assert.NotSame(transient(), transient());
		var first = graph();
		var second = graph();
		Assert.NotSame(first, second);
		Assert.NotSame(first.Movement, second.Movement);
		Assert.Same(singleton(), first.Clock);
		Assert.Same(first.Clock, first.Movement.Clock);
		Assert.Same(first.Clock, second.Clock);
	}
}
