using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Injectra.Benchmarks;

internal static class LifecycleMeasurements
{
	internal static void Run()
	{
		foreach (var participants in new[] { 0, 4 })
		foreach (var lifecycle in new[] { false, true })
		{
			// Warm construction, phase collection and dispatch before measuring retained child memory.
			using (var warm = new LifecycleFixture(lifecycle, 16, participants))
			{
			}
			var samples = new double[3];
			for (var i = 0; i < samples.Length; i++) samples[i] = Measure(lifecycle, participants);
			Array.Sort(samples);
			Console.WriteLine(JsonSerializer.Serialize(new
			{
				lifecycle, participants, children = 256, retainedBytesPerChild = samples[1],
				minimum = samples[0], maximum = samples[2]
			}));
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static double Measure(bool lifecycle, int participants)
	{
		const int batch = 256;
		using var fixture = new LifecycleFixture(lifecycle, 0, participants);
		var before = GC.GetTotalMemory(true);
		for (var i = 0; i < batch; i++) fixture.CreateChild();
		var bytes = GC.GetTotalMemory(true) - before;
		GC.KeepAlive(fixture);
		return (double)bytes / batch;
	}
}
