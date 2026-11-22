using System.Text;

namespace Injectra.Diagnostics;

/// <summary>Formats detached diagnostic snapshots as text trees.</summary>
public static class DiagnosticTree
{
	/// <summary>Formats registrations, constructor dependencies and child containers without accessing live services.</summary>
	/// <exception cref="ArgumentNullException">The snapshot is null.</exception>
	public static string Format(ContainerSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		var text = new StringBuilder();
		text.AppendLine($"Container #{snapshot.Id}");
		WriteContainer(snapshot, "");
		return text.ToString();

		void WriteContainer(ContainerSnapshot container, string prefix)
		{
			var total = container.Registrations.Count + container.Children.Count;
			var index = 0;
			foreach (var registration in container.Registrations)
			{
				var last = ++index == total;
				Line(prefix, last, RegistrationLabel(registration));
				var next = prefix + (last ? "    " : "│   ");
				WriteDependencies(registration.Dependencies, next, container.Id);
				if (registration.Activation == ActivationKind.Factory)
					Line(next, true, "Dependencies: unknown (factory)");
				else if (registration.ImplementationType.IsGenericTypeDefinition)
					Line(next, true, "Dependencies: deferred until generic closure");
			}
			foreach (var child in container.Children)
			{
				var last = ++index == total;
				Line(prefix, last, $"Container #{child.Id}");
				WriteContainer(child, prefix + (last ? "    " : "│   "));
			}
		}

		void WriteDependencies(IReadOnlyList<DependencySnapshot> dependencies, string prefix, int containerId)
		{
			for (var i = 0; i < dependencies.Count; i++)
			{
				var dependency = dependencies[i];
				var last = i == dependencies.Count - 1;
				var label = TypeName(dependency.ContractType);
				if (dependency.ImplementationType is null)
					label += dependency.IsAmbiguous ? " · Ambiguous" : " · Missing";
				else
				{
					if (dependency.ImplementationType != dependency.ContractType)
						label += $" → {TypeName(dependency.ImplementationType)}";
					label += dependency.DeclaringContainerId == containerId ? " · Local" : $" · Inherited: Container #{dependency.DeclaringContainerId}";
					if (dependency.IsReference)
						label += " · Reference (already expanded)";
				}
				Line(prefix, last, label);
				var next = prefix + (last ? "    " : "│   ");
				WriteDependencies(dependency.Dependencies, next, containerId);
				if (!dependency.IsReference && dependency.Activation == ActivationKind.Factory)
					Line(next, true, "Dependencies: unknown (factory)");
				else if (dependency.ImplementationType?.IsGenericTypeDefinition == true)
					Line(next, true, "Dependencies: deferred until generic closure");
			}
		}

		void Line(string prefix, bool last, string label) => text.Append(prefix).Append(last ? "└── " : "├── ").AppendLine(label);
	}

	private static string RegistrationLabel(RegistrationSnapshot registration)
	{
		var contracts = string.Join(", ", registration.Contracts.Select(TypeName));
		var implementation = TypeName(registration.ImplementationType);
		var label = $"{(registration.IsCreated ? "●" : "○")} {contracts}";
		if (registration.Contracts.Count != 1 || registration.Contracts[0] != registration.ImplementationType)
			label += $" → {implementation}";
		label += $" · {registration.Lifetime} · {registration.Activation}";
		label += registration.IsCreated ? " · Created" : " · Registered";
		label += registration.Ownership == InstanceOwnership.Container
			? $" · Owned: Container #{registration.OwnerContainerId}" : $" · {registration.Ownership}";
		if (registration.OverriddenContracts.Count != 0)
			label += $" · Override: {string.Join(", ", registration.OverriddenContracts.Select(TypeName))}";
		if (registration.IsSpecialization)
			label += $" · Cached generic (declared in Container #{registration.DeclaringContainerId})";
		return label;
	}

	private static string TypeName(Type type)
	{
		if (!type.IsGenericType)
			return type.Name;
		return $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(TypeName))}>";
	}
}
