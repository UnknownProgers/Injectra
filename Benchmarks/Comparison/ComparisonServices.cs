namespace Injectra.Benchmarks.Comparison;

// Identical, non-disposable objects for all containers: transient retention policies must not skew the comparison.
// Public types allow Grove's bundled source generator to use its normal constructor path.
public interface IClock;
public sealed class Clock : IClock;
public sealed class Enemy;
public sealed class Movement(IClock clock)
{
	public IClock Clock { get; } = clock;
}
public sealed class Player(IClock clock, Movement movement)
{
	public IClock Clock { get; } = clock;
	public Movement Movement { get; } = movement;
}
public interface IRepository<T>;
public sealed class Repository<T> : IRepository<T>;
public sealed class GenericConsumer<T>(IRepository<T> repository)
{
	public IRepository<T> Repository { get; } = repository;
}
