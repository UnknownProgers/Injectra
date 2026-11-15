using System.Reflection;
using Xunit;

namespace Injectra.Core.Tests.Resolution;

public sealed class ConstructorActivationTests
{
	[Theory]
	[InlineData(typeof(Zero), 0)]
	[InlineData(typeof(One), 1)]
	[InlineData(typeof(Two), 2)]
	[InlineData(typeof(Three), 3)]
	[InlineData(typeof(Four), 4)]
	[InlineData(typeof(Five), 5)]
	[InlineData(typeof(Eight), 8)]
	public void ArgumentsAreResolvedInOrderAndFreshForEveryInvocation(Type implementation, int arity)
	{
		var calls = 0;
		var builder = new ContainerBuilder();
		builder.Register(_ => new Dependency(++calls), Lifetime.Transient);
		builder.Register(implementation, Lifetime.Transient).As<IArguments>();
		using var container = builder.Build();

		var first = container.Resolve<IArguments>();
		var second = (IArguments)container.Resolve(typeof(IArguments));

		Assert.NotSame(first, second);
		Assert.Equal(Enumerable.Range(1, arity), first.Arguments.Select(argument => argument.Sequence));
		Assert.Equal(Enumerable.Range(arity + 1, arity), second.Arguments.Select(argument => argument.Sequence));
	}

	[Theory]
	[InlineData(typeof(ThrowsOne))]
	[InlineData(typeof(ThrowsFive))]
	public void UserThrownTargetInvocationExceptionIsNotUnwrappedAndCanBeRetried(Type implementation)
	{
		var error = new TargetInvocationException(new ApplicationException("User exception"));
		var failure = new Failure(error);
		var builder = new ContainerBuilder();
		builder.RegisterInstance(failure);
		builder.Register(implementation, Lifetime.Singleton);
		using var container = builder.Build();

		for (var i = 0; i < 2; i++)
			Assert.Same(error, Assert.Throws<TargetInvocationException>(() => container.Resolve(implementation)));
	}

	[Fact]
	public void CachedSingletonStillRejectsBothPublicResolvePathsAfterDisposal()
	{
		var builder = new ContainerBuilder();
		builder.Register<Zero>(Lifetime.Singleton).As<IArguments>();
		var container = builder.Build();
		var instance = container.Resolve<IArguments>();
		Assert.Same(instance, container.Resolve(typeof(IArguments)));

		container.Dispose();
		Assert.Throws<ObjectDisposedException>(() => container.Resolve<IArguments>());
		Assert.Throws<ObjectDisposedException>(() => container.Resolve(typeof(IArguments)));
	}

	public sealed record Dependency(int Sequence);
	public interface IArguments
	{
		Dependency[] Arguments { get; }
	}
	public sealed class Zero : IArguments
	{
		public Dependency[] Arguments => [];
	}
	public sealed class One(Dependency a) : IArguments
	{
		public Dependency[] Arguments { get; } = [a];
	}
	public sealed class Two(Dependency a, Dependency b) : IArguments
	{
		public Dependency[] Arguments { get; } = [a, b];
	}
	public sealed class Three(Dependency a, Dependency b, Dependency c) : IArguments
	{
		public Dependency[] Arguments { get; } = [a, b, c];
	}
	public sealed class Four(Dependency a, Dependency b, Dependency c, Dependency d) : IArguments
	{
		public Dependency[] Arguments { get; } = [a, b, c, d];
	}
	public sealed class Five(Dependency a, Dependency b, Dependency c, Dependency d, Dependency e) : IArguments
	{
		public Dependency[] Arguments { get; } = [a, b, c, d, e];
	}
	public sealed class Eight(Dependency a, Dependency b, Dependency c, Dependency d, Dependency e, Dependency f, Dependency g, Dependency h) : IArguments
	{
		public Dependency[] Arguments { get; } = [a, b, c, d, e, f, g, h];
	}
	public sealed record Failure(Exception Error);
	public sealed class ThrowsOne
	{
		public ThrowsOne(Failure failure) => throw failure.Error;
	}
	public sealed class ThrowsFive
	{
		public ThrowsFive(Failure a, Failure b, Failure c, Failure d, Failure e) => throw a.Error;
	}
}
