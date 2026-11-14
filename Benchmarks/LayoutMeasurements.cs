using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Injectra.Benchmarks;

// Retained managed heap is measured with containers kept alive across a full GC.
// Execution-time and allocation benchmarks are handled by BenchmarkDotNet.
internal static class LayoutMeasurements
{
	private static object? _sink;
	private static readonly Type[] Markers =
	[
		typeof(Marker0),
		typeof(Marker1),
		typeof(Marker2),
		typeof(Marker3),
		typeof(Marker4),
		typeof(Marker5),
		typeof(Marker6),
		typeof(Marker7),
		typeof(Marker8),
		typeof(Marker9),
		typeof(Marker10),
		typeof(Marker11),
		typeof(Marker12),
		typeof(Marker13),
		typeof(Marker14),
		typeof(Marker15),
		typeof(Marker16),
		typeof(Marker17),
		typeof(Marker18),
		typeof(Marker19),
		typeof(Marker20),
		typeof(Marker21),
		typeof(Marker22),
		typeof(Marker23),
		typeof(Marker24),
		typeof(Marker25),
		typeof(Marker26),
		typeof(Marker27),
		typeof(Marker28),
		typeof(Marker29),
		typeof(Marker30),
		typeof(Marker31),
	];
	internal static readonly Type[] Services = Markers.SelectMany(left => Markers.Select(right =>
		typeof(Service<>).MakeGenericType(typeof(Tag<,>).MakeGenericType(left, right)))).ToArray();

	internal static void Run()
	{
		const string variant = "working";
		foreach (var count in new[] { 32, 256, 1024 })
		foreach (var depth in new[] { 1, 4, 8 })
		{
			var builder = new ContainerBuilder();
			RegisterFillers(builder, count);
			builder.RegisterInstance<IInput>(new Input());
			builder.Register<Dependency>(Lifetime.Transient);
			builder.Register<Consumer>(Lifetime.Transient);
			using var root = builder.Build();
			// Warm root singleton creation so retained child bytes exclude root service allocations.
			for (var i = 0; i < count; i += 2) root.Resolve(Services[i]);
			var parent = root;
			for (var i = 1; i < depth; i++) parent = parent.CreateChild();
			using var leaf = parent.CreateChild(b => b.RegisterInstance<IInput>(new Input()));

			foreach (var touched in new[] { 0, 8, count })
				Retained(variant, count, depth, parent, touched);
		}
		GC.KeepAlive(_sink);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Retained(string variant, int count, int depth, Container parent, int touched)
	{
		const int batch = 64;
		var children = new Container[batch];
		_sink = null;
		var before = GC.GetTotalMemory(true);
		for (var i = 0; i < batch; i++)
		{
			var child = parent.CreateChild();
			children[i] = child;
			for (var service = 0; service < touched; service++) _sink = child.Resolve(Services[service]);
		}
		_sink = null;
		var retained = GC.GetTotalMemory(true) - before;
		Emit(new { variant, count, depth, operation = "retained-child", touched, bytes = (double)retained / batch });
		GC.KeepAlive(children);
		foreach (var child in children) child.Dispose();
	}

	private static void Emit<T>(T result) => Console.WriteLine(JsonSerializer.Serialize(result));

	internal static void RegisterFillers(ContainerBuilder builder, int count)
	{
		// Reflection is setup-only, outside every measurement.
		var method = typeof(LayoutMeasurements).GetMethod(nameof(RegisterFiller),
			BindingFlags.Static | BindingFlags.NonPublic)!;
		for (var i = 0; i < count; i++)
			method.MakeGenericMethod(Services[i]).Invoke(null, [builder, i]);
	}

	private static void RegisterFiller<T>(ContainerBuilder builder, int index) where T : class =>
		builder.Register<T>(index % 2 == 0 ? Lifetime.Singleton : Lifetime.Transient);

	public interface IInput;
	public sealed class Input : IInput;
	public sealed class Dependency(IInput input)
	{
		public IInput Input { get; } = input;
	}
	public sealed class Consumer(Dependency dependency, IInput input, IResolver resolver)
	{
		public Dependency Dependency { get; } = dependency;
		public IInput Input { get; } = input;
		public IResolver Resolver { get; } = resolver;
	}
	public sealed class Service<T>;
	public sealed class Tag<TLeft, TRight>;
	public sealed class Marker0;
	public sealed class Marker1;
	public sealed class Marker2;
	public sealed class Marker3;
	public sealed class Marker4;
	public sealed class Marker5;
	public sealed class Marker6;
	public sealed class Marker7;
	public sealed class Marker8;
	public sealed class Marker9;
	public sealed class Marker10;
	public sealed class Marker11;
	public sealed class Marker12;
	public sealed class Marker13;
	public sealed class Marker14;
	public sealed class Marker15;
	public sealed class Marker16;
	public sealed class Marker17;
	public sealed class Marker18;
	public sealed class Marker19;
	public sealed class Marker20;
	public sealed class Marker21;
	public sealed class Marker22;
	public sealed class Marker23;
	public sealed class Marker24;
	public sealed class Marker25;
	public sealed class Marker26;
	public sealed class Marker27;
	public sealed class Marker28;
	public sealed class Marker29;
	public sealed class Marker30;
	public sealed class Marker31;
}
