using Injectra.Lifecycle;
using Xunit;

namespace Injectra.Benchmarks.Tests;

public sealed class LifecycleBenchmarkTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void FixtureCreatesRealParticipantsAndClosesThem(bool lifecycle)
	{
		using var fixture = new LifecycleFixture(lifecycle, 8, 4);
		using var child = fixture.CreateChild();
		var participants = child.ResolveAll<IFrameTickable>().Cast<LifecycleParticipant>().ToArray();
		Assert.Equal(4, participants.Length);
		Assert.Equal(4, participants.Distinct().Count());
		if (lifecycle) fixture.Root.Resolve<InjectraLifecycle>().Tick(0.1);
		foreach (var participant in participants)
		{
			Assert.Equal(lifecycle ? 1 : 0, participant.Initializations);
			Assert.Equal(lifecycle ? 1 : 0, participant.Frames);
		}
		child.Dispose();
		if (lifecycle) fixture.Root.Resolve<InjectraLifecycle>().Tick(0.1);
		foreach (var participant in participants)
		{
			Assert.Equal(1, participant.Disposals);
			Assert.Equal(lifecycle ? 1 : 0, participant.Frames);
		}
	}
}
