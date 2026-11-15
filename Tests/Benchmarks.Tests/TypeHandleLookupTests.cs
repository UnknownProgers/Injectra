using Injectra.Benchmarks.Experiments;
using Xunit;

namespace Injectra.Benchmarks.Tests;

public sealed class TypeHandleLookupTests
{
	[Theory]
	[InlineData(LookupScenario.Local)]
	[InlineData(LookupScenario.Inherited)]
	[InlineData(LookupScenario.ClosedGeneric)]
	public void BothTablesResolveTheSameObject(LookupScenario scenario)
	{
		var benchmark = new TypeHandleLookupBenchmarks { Registrations = 32, Scenario = scenario };
		benchmark.Setup();
		Assert.Same(benchmark.TypeKey(), benchmark.HandleKey());
	}

	[Fact]
	public void BuildRetainsAllDistinctClosedGenericContracts()
	{
		var benchmark = new TypeHandleTableBuildBenchmarks { Registrations = 256 };
		benchmark.Setup();
		var types = benchmark.TypeKey();
		var handles = benchmark.HandleKey();
		Assert.Equal(types.Count, handles.Count);
		foreach (var (type, value) in types)
			Assert.Same(value, handles[type.TypeHandle]);
	}
}
