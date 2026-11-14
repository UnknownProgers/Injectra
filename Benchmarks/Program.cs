using BenchmarkDotNet.Running;

namespace Injectra.Benchmarks;

internal static class Program
{
	private static void Main(string[] args)
	{
		// Keep retained-heap measurements separate: MemoryDiagnoser measures allocations, not live heap size.
		if (args.Contains("--retained"))
		{
			LayoutMeasurements.Run();
			return;
		}
		BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
	}
}
