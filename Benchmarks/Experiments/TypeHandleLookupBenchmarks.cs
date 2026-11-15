using BenchmarkDotNet.Attributes;

namespace Injectra.Benchmarks.Experiments;

public enum LookupScenario
{
	Local,
	Inherited,
	ClosedGeneric
}

// Isolates current two-table lookup shape. Does not measure activation or parent traversal on a cold miss.
[MemoryDiagnoser]
public class TypeHandleLookupBenchmarks
{
	[Params(32, 256)]
	public int Registrations { get; set; }
	[Params(LookupScenario.Local, LookupScenario.Inherited, LookupScenario.ClosedGeneric)]
	public LookupScenario Scenario { get; set; }

	private Dictionary<Type, object> _types = null!;
	private Dictionary<Type, object> _typeCache = null!;
	private Dictionary<RuntimeTypeHandle, object> _handles = null!;
	private Dictionary<RuntimeTypeHandle, object> _handleCache = null!;
	private Type _requested = null!;

	[GlobalSetup]
	public void Setup()
	{
		_types = new(Registrations);
		_handles = new(Registrations);
		_typeCache = [];
		_handleCache = [];
		foreach (var type in LayoutMeasurements.Services.Take(Registrations))
		{
			var entry = new object();
			_types.Add(type, entry);
			_handles.Add(type.TypeHandle, entry);
		}
		_requested = Scenario == LookupScenario.ClosedGeneric ? typeof(List<Guid>) : typeof(PlainService);
		var value = new object();
		if (Scenario == LookupScenario.Local)
		{
			_types.Add(_requested, value);
			_handles.Add(_requested.TypeHandle, value);
		}
		else
		{
			_typeCache.Add(_requested, value);
			_handleCache.Add(_requested.TypeHandle, value);
		}
	}

	[Benchmark(Baseline = true)]
	public object TypeKey() => _types.TryGetValue(_requested, out var entry) ? entry : _typeCache[_requested];

	[Benchmark]
	public object HandleKey()
	{
		// Include conversion on every request, rather than benchmarking an artificially precomputed handle.
		var handle = _requested.TypeHandle;
		return _handles.TryGetValue(handle, out var entry) ? entry : _handleCache[handle];
	}

	private sealed class PlainService;
}

[MemoryDiagnoser]
public class TypeHandleTableBuildBenchmarks
{
	[Params(32, 256)]
	public int Registrations { get; set; }
	private Type[] _types = null!;
	private readonly object _entry = new();

	[GlobalSetup]
	public void Setup() => _types = LayoutMeasurements.Services.Take(Registrations).ToArray();

	[Benchmark(Baseline = true)]
	public Dictionary<Type, object> TypeKey()
	{
		var table = new Dictionary<Type, object>(Registrations);
		foreach (var type in _types)
			table.Add(type, _entry);
		return table;
	}

	[Benchmark]
	public Dictionary<RuntimeTypeHandle, object> HandleKey()
	{
		var table = new Dictionary<RuntimeTypeHandle, object>(Registrations);
		foreach (var type in _types)
			table.Add(type.TypeHandle, _entry);
		return table;
	}
}
