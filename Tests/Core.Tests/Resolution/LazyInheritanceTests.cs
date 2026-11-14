using System.Runtime.CompilerServices;
using Xunit;

namespace Injectra.Core.Tests.Resolution;

public sealed class LazyInheritanceTests
{
	[Fact]
	public void CachedInheritedPlanDoesNotRetainTransientInstances()
	{
		var builder = new ContainerBuilder();
		builder.Register<TransientResource>(Lifetime.Transient);
		using var root = builder.Build();
		using var child = root.CreateChild();
		var reference = ResolveTransient(child);
		Collect();
		Assert.False(reference.IsAlive);
		GC.KeepAlive(child);
	}

	[Fact]
	public void ClosedChildReleasesCachedReferencesToItsParent()
	{
		var (child, parent) = CreateClosedChild();
		Collect();
		Assert.False(parent.IsAlive);
		GC.KeepAlive(child);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference ResolveTransient(Container child) => new(child.Resolve<TransientResource>());

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static (Container Child, WeakReference Parent) CreateClosedChild()
	{
		var builder = new ContainerBuilder();
		builder.Register<ParentAware>(Lifetime.Singleton);
		using var root = builder.Build();
		var child = root.CreateChild();
		child.Resolve<ParentAware>();
		child.Dispose();
		return (child, new WeakReference(root));
	}

	private static void Collect()
	{
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
	}

	public sealed class TransientResource : IDisposable
	{
		public void Dispose()
		{
		}
	}

	public sealed class ParentAware(IResolver resolver)
	{
		public IResolver Resolver { get; } = resolver;
	}
}
