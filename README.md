# Injectra

A lightweight dependency injection library for .NET, designed primarily for game development. Inspired
by [VContainer](https://github.com/hadashiA/VContainer), [Zenject](https://github.com/modesttree/Zenject),
and [Microsoft.Extensions.DependencyInjection](https://github.com/dotnet/runtime/tree/main/src/libraries/Microsoft.Extensions.DependencyInjection).

Injectra focuses on explicit registration, constructor injection, and predictable lifetimes with a small API. The core is engine-independent
C#.

Currently in alpha. Targets .NET 11; building the repository requires the SDK specified in `global.json`.

## Features

- Fluent registrations: constructors, factories, existing instances, and multiple contracts.
- Singleton and transient lifetimes, with optional `.NonLazy()` singleton creation during build.
- Child containers with local overrides and lazy preparation of inherited transient graphs.
- Open generic registrations, closed on demand.
- Source-generated constructor calls for supported types, with a reflection fallback.
- Explicit ownership of existing instances and automatic disposal of container-owned singletons.
- Validation of duplicate concrete registrations, missing or ambiguous dependencies, and constructor cycles; dynamic factory cycles are detected during
  resolution.
- No Godot or Unity dependency. Concurrent container use is unsupported.

## Quick start

Register services at the application or scene entry point, build the container, then resolve the root object. Its constructor dependencies
are resolved automatically.

```csharp
using Injectra;

var builder = new ContainerBuilder();
builder.Register<KeyboardInput>(Lifetime.Singleton)
	.As<IInput>();
builder.Register<Player>(Lifetime.Transient);

using var container = builder.Build();
var player = container.Resolve<Player>();
player.Tick();

public interface IInput
{
	void Poll();
}

public sealed class KeyboardInput : IInput
{
	public void Poll() { }
}

public sealed class Player(IInput input)
{
	public void Tick() => input.Poll();
}
```

Constructor-based services must have exactly one public constructor. No injection attribute is required.

`Build()` validates and prepares local constructor graphs. Services are created lazily during resolution unless explicitly marked
`.NonLazy()`. A successful build consumes the builder: registrations cannot be changed or built again.

### Contracts and lifetimes

A registration exposes its implementation type by default. Calling `.As<T>()` replaces that implicit self contract; use `.AsSelf()` to
expose both. All contracts of a registration share its lifetime and singleton instance.

```csharp
var builder = new ContainerBuilder();
builder.Register<KeyboardInput>(Lifetime.Singleton)
	.As<IInput>()
	.AsSelf();

using var container = builder.Build();
var input = container.Resolve<IInput>();
var keyboard = container.Resolve<KeyboardInput>();
// input and keyboard refer to the same object.
```

| Lifetime    | Behavior                                                                                        |
|-------------|-------------------------------------------------------------------------------------------------|
| `Singleton` | One instance per registration, shared with descendants and owned by the container declaring it. |
| `Transient` | A new instance for each resolution. The container does not retain or dispose it.                |

### Interface registrations

Use `.AsImplementedInterfaces()` to expose every implemented interface, including inherited ones. Add `.AsSelf()` to retain
access through the concrete type. This does not change ownership. For open generic types, all interfaces must support positional
mapping; unsupported mappings are rejected without partially changing the registration.

Several registrations may expose the same interface. `container.ResolveAll<IInput>()` returns all matching registrations with their
individual lifetimes. A single `Resolve<IInput>()` throws if the contract is ambiguous; ambiguous constructor dependencies fail during
graph preparation. Unregistered closed contracts return an empty list from `ResolveAll`. Duplicate concrete registrations remain invalid.
A child with local registrations overrides the inherited set rather than merging with it. Result order is unspecified.

### Eager singleton creation

Use `.NonLazy()` to create a singleton and its dependencies during `Build()`, after registration validation and dependency preparation:

```csharp
var builder = new ContainerBuilder();
builder.Register<KeyboardInput>(Lifetime.Singleton).As<IInput>();
builder.Register<Player>(Lifetime.Singleton)
	.NonLazy();

using var container = builder.Build();
var player = container.Resolve<Player>();
// Player was already created by Build(); Resolve returns that same instance.
```

Only singletons can be marked `.NonLazy()`. Open generic definitions cannot be marked eager; register a closed type instead. Existing instances are already created, so the marker has no additional effect on them. This controls creation, not lifecycle callbacks.

Eager roots are requested in registration order; constructor dependencies are created first as needed. Child containers create only their own eager registrations. Inherited singletons remain owned by their declaring container.

Validation failures leave the builder mutable. Once eager creation starts, the builder is consumed even if a constructor or factory fails. On failure, the container and its owned instances are disposed; externally owned instances are left alone. If cleanup also fails, both failures are reported in an `AggregateException`.

### Factories

Factories receive the current resolver and run when the service is requested, rather than during registration or build.

```csharp
var builder = new ContainerBuilder();
builder.Register<IInput>(_ => new KeyboardInput(), Lifetime.Singleton);
builder.Register(resolver => new Player(resolver.Resolve<IInput>()), Lifetime.Transient);

using var container = builder.Build();
var player = container.Resolve<Player>();
```

### Child containers

Use a child for a level, match, or another independently configured lifetime. Disposing the parent also disposes its children.

```csharp
var builder = new ContainerBuilder();
builder.Register<KeyboardInput>(Lifetime.Singleton).As<IInput>();
builder.Register<Player>(Lifetime.Transient);

using var application = builder.Build();
using var level = application.CreateChild(child =>
	child.Register<ReplayInput>(Lifetime.Singleton).As<IInput>());

var player = level.Resolve<Player>();
// The inherited transient Player receives this level's ReplayInput.

public sealed class ReplayInput : IInput
{
	public void Poll() { }
}
```

Inherited transients use the requesting child's dependencies. Parent singletons keep their owner's dependencies: a child's override does not
rebuild a parent singleton. A singleton registered in the child lives with that child.

### Existing instances and disposal

An existing instance remains externally owned by default:

```csharp
using var stream = new MemoryStream();
var builder = new ContainerBuilder();
builder.RegisterInstance(stream);

using var container = builder.Build();
// The caller disposes stream; the container does not.
```

Transfer ownership explicitly when the container should dispose the instance:

```csharp
var builder = new ContainerBuilder();
builder.RegisterInstance(new MemoryStream())
	.OwnedByContainer();

using var container = builder.Build();
// The container disposes the stream, even if it was never resolved.
```

Constructed singleton services implementing `IDisposable` are disposed automatically, regardless of the contracts exposed by their
registration. Container-owned disposables are released in reverse acquisition order, after children. Transient cleanup belongs to the
caller.

### Open generics

```csharp
var builder = new ContainerBuilder();
builder.Register(typeof(Repository<>), Lifetime.Singleton)
	.As(typeof(IRepository<>));

using var container = builder.Build();
var repository = container.Resolve<IRepository<int>>();
// Each closed implementation has its own singleton instance.

public interface IRepository<T>;
public sealed class Repository<T> : IRepository<T>;
```

### Source generation

The core package includes a Roslyn generator that emits direct constructor calls for supported registrations and resolvable types visible to
it. Open generic inference is supported for discoverable closed types. Types the generator cannot handle use reflection-based activation.

No separate registration syntax is needed. When referencing the projects directly, add the generator as an analyzer, as shown
in [Core.Tests.csproj](Tests/Core.Tests/Core.Tests.csproj).

## Performance

Latest recorded comparison
using [Grove's benchmark scenarios](https://github.com/grovegs/DependencyInjection/blob/9bac3d464f38057127b0c904545e5c056a415728/sandbox/DotnetBenchmark/ResolveBenchmark.cs),
with equivalent Injectra registrations. The implementation is in [ContainerComparisonBenchmarks.cs](Benchmarks/Comparison/ContainerComparisonBenchmarks.cs).

| Benchmark                                   |    Injectra |    Grove | Microsoft DI |
|---------------------------------------------|------------:|---------:|-------------:|
| Singleton resolve                           | **2.63 ns** |  5.56 ns |      3.85 ns |
| Transient resolve, 3 singleton dependencies |     9.22 ns | 18.18 ns |  **6.08 ns** |
| Factory resolve                             | **6.96 ns** |  8.97 ns |      7.76 ns |
| Build, 4 registrations                      |  **328 ns** |   708 ns |       654 ns |

| Managed allocation per operation | Injectra |  Grove | Microsoft DI |
|----------------------------------|---------:|-------:|-------------:|
| Singleton resolve                |      0 B |    0 B |          0 B |
| Transient resolve                |     24 B |   24 B |         24 B |
| Factory resolve                  |     24 B |   24 B |         24 B |
| Build                            |   2472 B | 3248 B |       6224 B |

Measured on November 15, 2026, using an AMD Ryzen 7 9800X3D on Linux CachyOS, .NET SDK 11.0.100 / .NET 11.0.0, BenchmarkDotNet
0.16.0-preview.2, Grove 0.8.1, and Microsoft.Extensions.DependencyInjection 11.0.0.

These are short-run microbenchmarks: one launch, three warmup iterations, and five measured iterations targeting 100 ms each. Resolve
scenarios use `Resolve(Type)` / `GetRequiredService(Type)` on warmed containers. The transient constructor accepts three singleton
dependencies and stores no fields. Build includes four registrations and container construction, with no resolution or disposal; Microsoft
uses default provider options. Source generators are enabled where applicable. Results describe these scenarios, rather than all application
workloads.

Run the same short comparison from the repository root:

```sh
dotnet run --project Benchmarks -c Release -- \
	--filter '*ContainerComparisonBenchmarks*' \
	--job short --iterationCount 5 --iterationTime 100 --warmupCount 3
```
