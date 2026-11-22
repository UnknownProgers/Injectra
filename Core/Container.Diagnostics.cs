using Injectra.Diagnostics;
using Injectra.Internal.Activation;
using Injectra.Internal.Resolution;

namespace Injectra;

public sealed partial class Container
{
	/// <summary>Captures this container and its live descendants without creating services or changing resolution caches.</summary>
	/// <returns>A detached snapshot, sorted created-first within each container, with no service-instance references.</returns>
	/// <exception cref="ObjectDisposedException">This container has been disposed.</exception>
	/// <remarks>
	/// Main-thread use only, like resolution. IDs are local to this capture's hierarchy and may change between captures.
	/// Inherited registrations are referenced through dependency sources rather than copied into child registration lists.
	/// Factory dependencies are unknown. Open generic definitions are not closed for inspection.
	/// </remarks>
	public ContainerSnapshot GetDiagnostics()
	{
		ThrowIfDisposed();
		return new DiagnosticCapture(this).Capture(this);
	}

	private sealed class DiagnosticCapture
	{
		private readonly Dictionary<Container, int> _ids = [];

		internal DiagnosticCapture(Container container)
		{
			while (container._parent is not null)
				container = container._parent;
			AssignIds(container);
		}

		private void AssignIds(Container container)
		{
			_ids.Add(container, _ids.Count + 1);
			foreach (var child in container._children)
				AssignIds(child);
		}

		internal ContainerSnapshot Capture(Container container)
		{
			var contracts = LocalContracts(container);
			var registrations = new List<RegistrationSnapshot>();
			foreach (var (binding, aliases) in contracts)
				registrations.Add(CaptureRegistration(container, binding, aliases, binding.Owner, false));

			if (container._closedGenerics is not null)
				foreach (var (key, binding) in container._closedGenerics)
				{
					var definitionContracts = LocalContracts(key.Definition.Owner)[key.Definition];
					var aliases = definitionContracts.Select(type => type.MakeGenericType(key.Implementation.GetGenericArguments())).ToList();
					registrations.Add(CaptureRegistration(container, binding, aliases, key.Definition.Owner, true));
				}

			var sorted = registrations.OrderByDescending(entry => entry.IsCreated)
				.ThenBy(entry => entry.ImplementationType.FullName, StringComparer.Ordinal).ToArray();
			return new ContainerSnapshot(_ids[container], container._parent is null ? null : _ids[container._parent],
				Array.AsReadOnly(sorted), Array.AsReadOnly(container._children.Select(Capture).ToArray()));
		}

		private RegistrationSnapshot CaptureRegistration(Container context, Binding binding, List<Type> aliases, Container declaring, bool specialization)
		{
			var kind = CreationKind(binding);
			var owner = binding.Lifetime == Lifetime.Singleton ? binding.Owner : null;
			if (binding.Instance is { } instance)
			{
				owner = null;
				for (var ancestor = context; ancestor is not null; ancestor = ancestor._parent)
					if (ancestor._ownedInstances.Contains(instance))
					{
						owner = ancestor;
						break;
					}
			}

			var ownership = InstanceOwnership.Container;
			if (binding.Lifetime == Lifetime.Transient)
				ownership = InstanceOwnership.Consumer;
			else if (owner is null)
				ownership = InstanceOwnership.External;
			var overrides = aliases.Where(type => HasAncestorContract(declaring, type)).OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();
			var visited = new HashSet<(Binding, Container)> { (binding, context) };
			return new RegistrationSnapshot(binding.ImplementationType,
				Array.AsReadOnly(aliases.OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray()),
				binding.Lifetime, kind, binding.Instance is not null, ownership, owner is null ? null : _ids[owner],
				_ids[declaring], specialization, Array.AsReadOnly(overrides), CaptureDependencies(binding, context, visited));
		}

		private IReadOnlyList<DependencySnapshot> CaptureDependencies(Binding binding, Container context, HashSet<(Binding, Container)> visited)
		{
			if (binding.Activation is null || binding.ImplementationType.IsGenericTypeDefinition)
				return Array.Empty<DependencySnapshot>();

			var dependencies = new List<DependencySnapshot>();
			foreach (var type in binding.Activation.DependencyTypes)
			{
				var (source, candidates) = InspectContract(context, type);
				if (candidates is null || candidates.Length != 1)
				{
					dependencies.Add(new DependencySnapshot(type, null, source is null ? null : _ids[source], null,
						false, candidates is { Length: > 1 }, Array.Empty<DependencySnapshot>()));
					continue;
				}

				var dependency = candidates[0];
				var nextContext = dependency.Lifetime == Lifetime.Singleton ? dependency.Owner : context;
				var repeated = !visited.Add((dependency, nextContext));
				var nested = repeated ? Array.Empty<DependencySnapshot>() : CaptureDependencies(dependency, nextContext, visited);
				dependencies.Add(new DependencySnapshot(type, dependency.ImplementationType, _ids[source!], CreationKind(dependency), repeated, false, nested));
			}
			return dependencies.AsReadOnly();
		}
	}

	private static Dictionary<Binding, List<Type>> LocalContracts(Container container)
	{
		var result = new Dictionary<Binding, List<Type>>();
		foreach (var (handle, bindings) in container._bindings)
			foreach (var binding in bindings)
			{
				if (!result.TryGetValue(binding, out var aliases))
					result.Add(binding, aliases = []);
				aliases.Add(Type.GetTypeFromHandle(handle)!);
			}
		return result;
	}

	private static (Container? Source, Binding[]? Bindings) InspectContract(Container context, Type type)
	{
		if (type == typeof(IResolver))
			return (context, context._resolverBindings);

		for (var current = context; current is not null; current = current._parent)
		{
			var bindings = current.FindLocalBindings(type);
			if (bindings is null)
				continue;

			if (type.IsConstructedGenericType && bindings.Length == 1 && bindings[0].ImplementationType.IsGenericTypeDefinition)
			{
				var definition = bindings[0];
				var owner = definition.Lifetime == Lifetime.Singleton ? definition.Owner : context;
				if (owner._closedGenerics is not null)
					foreach (var (key, closed) in owner._closedGenerics)
						if (ReferenceEquals(key.Definition, definition)
							&& key.Implementation.GetGenericArguments().SequenceEqual(type.GetGenericArguments()))
							return (current, [closed]);
			}
			return (current, bindings);
		}
		return (null, null);
	}

	private static bool HasAncestorContract(Container container, Type type) =>
		container._parent is not null && InspectContract(container._parent, type).Bindings is not null;

	private static ActivationKind CreationKind(Binding binding) => binding.Activation switch
	{
		null => ActivationKind.Instance,
		FactoryActivation => ActivationKind.Factory,
		_ => ActivationKind.Constructor
	};
}
