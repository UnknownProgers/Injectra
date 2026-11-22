using Xunit;

namespace Injectra.Lifecycle.Tests;

public sealed class ContainerPreparationTests
{
	[Fact]
	public void RootAndDescendantsInitializeOnlyAfterTheirPreparationCompletes()
	{
		var events = new List<string>();
		var root = new Participant("root", events);
		var child = new Participant("child", events);
		var grandchild = new Participant("grandchild", events);
		var builder = new ContainerBuilder().UseLifecycle();
		builder.RegisterInstance(root).AsImplementedInterfaces();
		using var container = builder.Build(built =>
		{
			Assert.Throws<InvalidOperationException>(() => built.Resolve<InjectraLifecycle>().Initialize());
			root.Prepare();
			built.CreateChild(b => b.RegisterInstance(child).AsImplementedInterfaces(), prepared =>
			{
				prepared.CreateChild(b => b.RegisterInstance(grandchild).AsImplementedInterfaces(), _ => grandchild.Prepare());
				child.Prepare();
			});
		});
		Assert.Equal(["root:prepare", "grandchild:prepare", "child:prepare"], events);
		container.Resolve<InjectraLifecycle>().Initialize();
		Assert.All(new[] { root, child, grandchild }, participant => Assert.Equal(1, participant.Initializations));
	}

	[Fact]
	public void ReadyLifecycleAttachesAnEntirePreparedBranchAfterItsOuterCallback()
	{
		var events = new List<string>();
		using var container = new ContainerBuilder().UseLifecycle().Build();
		var lifecycle = container.Resolve<InjectraLifecycle>();
		lifecycle.Initialize();
		var child = new Participant("child", events);
		var grandchild = new Participant("grandchild", events);
		using var branch = container.CreateChild(b => b.RegisterInstance(child).AsImplementedInterfaces(), prepared =>
		{
			prepared.CreateChild(b => b.RegisterInstance(grandchild).AsImplementedInterfaces(), _ => grandchild.Prepare());
			Assert.Equal(0, grandchild.Initializations);
			child.Prepare();
		});
		Assert.Equal(["grandchild:prepare", "child:prepare", "child:initialize", "grandchild:initialize"], events);
	}

	[Fact]
	public void CreationDuringTickPreparesImmediatelyButInitializesAfterTraversal()
	{
		var events = new List<string>();
		var created = new Participant("child", events);
		Container? child = null;
		Container? container = null;
		var creator = new FrameAction(() =>
		{
			if (child is not null) return;
			child = container!.CreateChild(b => b.RegisterInstance(created).AsImplementedInterfaces(), _ => created.Prepare());
			Assert.Equal(0, created.Initializations);
			events.Add("tick:end");
		});
		var builder = new ContainerBuilder().UseLifecycle();
		builder.RegisterInstance(creator).As<IFrameTickable>();
		using (container = builder.Build())
		{
			var lifecycle = container.Resolve<InjectraLifecycle>();
			lifecycle.Initialize();
			lifecycle.Tick(0.1);
			Assert.Equal(["child:prepare", "tick:end", "child:initialize"], events);
			lifecycle.Tick(0.1);
			Assert.Equal("child:tick", events[^1]);
		}
	}

	[Fact]
	public void FailedPreparationDisposesTheWholeBranchWithoutInitializingIt()
	{
		using var container = new ContainerBuilder().UseLifecycle().Build();
		container.Resolve<InjectraLifecycle>().Initialize();
		var events = new List<string>();
		var child = new Participant("child", events);
		var grandchild = new Participant("grandchild", events);
		Assert.Throws<InvalidOperationException>(() => container.CreateChild(
			b => b.RegisterInstance(child).AsImplementedInterfaces().OwnedByContainer(), prepared =>
			{
				prepared.CreateChild(b => b.RegisterInstance(grandchild).AsImplementedInterfaces().OwnedByContainer(), _ => grandchild.Prepare());
				throw new InvalidOperationException("Preparation failed.");
			}));
		Assert.Equal(0, child.Initializations);
		Assert.Equal(0, grandchild.Initializations);
		Assert.Equal(1, child.Disposals);
		Assert.Equal(1, grandchild.Disposals);
		container.Resolve<InjectraLifecycle>().Tick(0.1);
	}

	[Fact]
	public void RootPreparationFailureConsumesBuilderAndDisposesItsServices()
	{
		var participant = new Participant("root", []);
		var builder = new ContainerBuilder().UseLifecycle();
		builder.RegisterInstance(participant).AsImplementedInterfaces().OwnedByContainer();
		Assert.Throws<InvalidOperationException>(() => builder.Build(_ => throw new InvalidOperationException()));
		Assert.Equal(1, participant.Disposals);
		Assert.Equal(0, participant.Initializations);
		Assert.Throws<InvalidOperationException>(() => builder.Build());
	}

	[Fact]
	public void NullPreparationCallbacksAreRejected()
	{
		var builder = new ContainerBuilder();
		Assert.Throws<ArgumentNullException>(() => builder.Build(null!));
		using var container = builder.Build();
		Assert.Throws<ArgumentNullException>(() => container.CreateChild(null, null!));
	}

	private sealed class Participant(string name, List<string> events) : IInitializable, IFrameTickable, IDisposable
	{
		private bool _prepared;
		public int Initializations { get; private set; }
		public int Disposals { get; private set; }
		public void Prepare() { _prepared = true; events.Add($"{name}:prepare"); }
		public void Initialize()
		{
			Assert.True(_prepared);
			Initializations++;
			events.Add($"{name}:initialize");
		}
		public void FrameTick(double deltaTime) { Assert.Equal(1, Initializations); events.Add($"{name}:tick"); }
		public void Dispose() => Disposals++;
	}

	private sealed class FrameAction(Action action) : IFrameTickable
	{
		public void FrameTick(double deltaTime) => action();
	}
}
