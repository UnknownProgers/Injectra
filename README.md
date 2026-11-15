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

## Lifecycle integration

Reference `Injectra.Lifecycle` to enable lifecycle dispatch for a root container. Register custom phases through `UseLifecycle`:

```csharp
using Injectra.Lifecycle;

var builder = new ContainerBuilder();
builder.UseLifecycle(lifecycle =>
	lifecycle.RegisterPhase<IPhysicsTick, double>(
		static (service, delta) => service.PhysicsTick(delta)));
builder.Register<GameSession>(Lifetime.Singleton)
	.AsImplementedInterfaces();

using var container = builder.Build();
var lifecycle = container.Resolve<InjectraLifecycle>();
lifecycle.Initialize();

// Your game loop or engine adapter supplies the timing:
lifecycle.Tick(1.0 / 60);
lifecycle.Dispatch<IPhysicsTick, double>(1.0 / 60);

public interface IPhysicsTick
{
	void PhysicsTick(double deltaTime);
}

public sealed class GameSession : IInitializable, IFrameTickable, IPhysicsTick
{
	public void Initialize() { }
	public void FrameTick(double deltaTime) { }
	public void PhysicsTick(double deltaTime) { }
}
```

Only explicitly exposed contracts participate. `.As<IFrameTickable>()` enables frame callbacks but does not enable `Initialize()` just
because the class implements `IInitializable`. Use `.As<IInitializable>()` for initialization, or `.AsImplementedInterfaces()` for both.
The same rule applies to custom phases. An instance may participate in several phases; registering the same instance twice within one
phase is an error. Participant order is unspecified.

The manager is an owned singleton, created during Build. Each container has an internal dispatcher that collects only its local
participants. Empty phases allocate no phase object, participant set, or local phase dictionary. Phase configuration runs during
Build and is then frozen. Initialization remains an explicit call after Build. Participating services must be singleton
or instance registrations; transients are rejected. The container disposes its owned services, while existing instances retain their
configured ownership. The manager does not dispose participants itself.

Call `UseLifecycle` once on the root builder. Children and grandchildren automatically join the same manager with their local
lifecycle registrations; inherited participants are not added again. Calling `UseLifecycle` on a child fails during Build.
Concurrent use and recursive phase dispatch are unsupported.

### Creating and closing children

| Operation | Behaviour |
|---|---|
| Create a child before `Initialize()` | Prepare its participants; initialize them during the root manager's `Initialize()`. |
| Create a child after initialization, outside a callback | Prepare and initialize its participants before `CreateChild()` returns. |
| Create a child inside a lifecycle callback | Return the built container immediately; attach and initialize participants after the current traversal. |
| Close a child outside a callback | Detach participants and dispose owned services synchronously. |
| Close a child inside a lifecycle callback | Stop all dispatchers in its subtree and dispose owned services immediately. |

New participants start receiving callbacks with the next phase call, which can be another phase in the same frame.
Inside the callback that creates a child, its services can be resolved, but their lifecycle initialization has not run yet.
The queue is processed synchronously before the enclosing `Tick`, `Dispatch`, or `Initialize` returns; no background work is involved.
Children created by deferred initializers are processed by the same queue.

Use the usual `Dispose()` to close a level, including from its own `FrameTick`:

```csharp
// Finish work that needs services from this level first.
level.Dispose();
return; // Do not use disposed services after the call.
```

Disposal is synchronous: code after `Dispose()` runs with the subtree already disposed. This also applies to callers higher in
the call stack, so arrange control flow accordingly. Remaining callbacks in the subtree are skipped, including participants in
grandchildren. Other dispatchers continue unless a callback throws. Repeated disposal calls have no effect.
A child created and closed during the same traversal never joins the lifecycle. Owned descendants are disposed before their parent
services; external instances retain their configured ownership. All local dispatchers in the subtree stop before the first service
is disposed. The common manager removes stopped dispatcher entries after traversal; it never completes container disposal.
Root container disposal and direct manager disposal remain forbidden during lifecycle callbacks; shut down the application after
returning to the game loop.

### Exceptions

- A participant exception stops the current traversal. Pending child attachments are still processed, then the exception propagates with its
  original stack trace. Later ticks remain allowed after a tick failure.
- Failed initial traversal leaves the manager faulted; initialization cannot be retried. Pending children are disposed instead of
  being attached. There is no disposal queue.
- Deferred child setup failures dispose the failed child. The already initialized manager remains usable.
- Child setup errors propagate with their original stack trace. Multiple errors (including a callback error and a child setup error)
  are combined in `AggregateException`. Remaining pending children and owned-object cleanup are still attempted.
- Disposal errors are raised synchronously by `Dispose()` as `AggregateException`. If unhandled inside a lifecycle callback, that
  callback stops and the error propagates from the lifecycle call.
- Initialization already performed is not rolled back. Child attachment requested during a callback happens after traversal, so
  attachment errors are reported by that lifecycle call rather than the original `CreateChild()`.

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
