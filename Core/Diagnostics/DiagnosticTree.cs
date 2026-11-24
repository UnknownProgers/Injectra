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
			var groups = container.Registrations.Where(registration => GenericContract(registration) is null)
				.Select(registration => (Family: (Type?)null, Members: new[] { registration }))
				.Concat(container.Registrations.Where(registration => GenericContract(registration) is not null)
					.GroupBy(registration => GenericContract(registration))
					.Select(group => (Family: group.Key, Members: group.ToArray())))
				.OrderByDescending(group => group.Members.Any(registration => registration.IsCreated))
				.ThenBy(group => TypeName(group.Family ?? group.Members[0].ImplementationType), StringComparer.OrdinalIgnoreCase)
				.ToArray();
			var total = groups.Length + container.Children.Count;
			var index = 0;
			foreach (var child in container.Children.OrderBy(child => child.Id))
			{
				var last = ++index == total;
				Line(prefix, last, $"Container #{child.Id}");
				WriteContainer(child, prefix + (last ? "    " : "│   "));
			}
			foreach (var group in groups)
			{
				var last = ++index == total;
				if (group.Family is null)
				{
					WriteRegistration(group.Members[0], prefix, last, container.Id);
					continue;
				}
				Line(prefix, last, TypeName(group.Family));
				var members = group.Members.OrderByDescending(registration => registration.IsCreated)
					.ThenBy(registration => TypeName(registration.ImplementationType), StringComparer.OrdinalIgnoreCase).ToArray();
				var next = prefix + (last ? "    " : "│   ");
				for (var i = 0; i < members.Length; i++)
					WriteRegistration(members[i], next, i == members.Length - 1, container.Id);
			}
		}

		void WriteRegistration(RegistrationSnapshot registration, string prefix, bool last, int containerId)
		{
			Line(prefix, last, RegistrationLabel(registration));
			var next = prefix + (last ? "    " : "│   ");
			var deferred = registration.Activation == ActivationKind.Factory || registration.ImplementationType.IsGenericTypeDefinition;
			Line(next, registration.Dependencies.Count == 0 && !deferred,
				$"Contracts: {string.Join(", ", registration.Contracts.Select(TypeName).Order(StringComparer.OrdinalIgnoreCase))}");
			WriteDependencies(registration.Dependencies, next, containerId);
			if (registration.Activation == ActivationKind.Factory)
				Line(next, true, "Dependencies: unknown (factory)");
			else if (registration.ImplementationType.IsGenericTypeDefinition)
				Line(next, true, "Dependencies: deferred until generic closure");
		}

		void WriteDependencies(IReadOnlyList<DependencySnapshot> dependencies, string prefix, int containerId)
		{
			var sorted = dependencies.OrderBy(dependency => TypeName(dependency.ImplementationType ?? dependency.ContractType), StringComparer.OrdinalIgnoreCase).ToArray();
			for (var i = 0; i < sorted.Length; i++)
			{
				var dependency = sorted[i];
				var last = i == sorted.Length - 1;
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
		var label = $"{(registration.IsCreated ? "●" : "○")} {TypeName(registration.ImplementationType)}";
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

	private static Type? GenericContract(RegistrationSnapshot registration)
	{
		var contract = registration.Contracts.Where(type => type.IsGenericType)
			.OrderByDescending(type => type.IsInterface)
			.ThenBy(TypeName, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
		return contract?.GetGenericTypeDefinition();
	}

	private static string TypeName(Type type)
	{
		if (!type.IsGenericType)
			return type.Name;
		var marker = type.Name.IndexOf('`');
		var name = marker < 0 ? type.Name : type.Name[..marker];
		if (type.IsGenericTypeDefinition)
			return $"{name}<{new string(',', type.GetGenericArguments().Length - 1)}>";
		return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(TypeName))}>";
	}
}
