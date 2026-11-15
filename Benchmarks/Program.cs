using BenchmarkDotNet.Running;

namespace Injectra.Benchmarks;

internal static class Program
{
	private static void Main(string[] args)
	{
#if !CORE_ASSEMBLY_OVERRIDE
		if (args.Contains("--lifecycle-retained"))
		{
			LifecycleMeasurements.Run();
			return;
		}
#endif
		// Keep retained-heap measurements separate: MemoryDiagnoser measures allocations, not live heap size.
		if (args.Contains("--retained"))
		{
			LayoutMeasurements.Run();
			return;
		}
		BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
	}
}
