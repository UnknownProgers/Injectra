namespace Injectra.Lifecycle.Internal;

internal sealed class Participants<T>(T[] root) where T : class
{
	private readonly HashSet<T> _known = new(root, ReferenceEqualityComparer.Instance);
	internal T[] Root { get; } = root;
	internal Dictionary<Container, T[]> Children { get; } = [];

	internal void Attach(Container container)
	{
		var added = container.ResolveLocalSingletons<T>();
		var unique = new HashSet<T>(ReferenceEqualityComparer.Instance);
		foreach (var participant in added)
			if (_known.Contains(participant) || !unique.Add(participant))
				throw new RegistrationException($"The same participant is registered more than once for {typeof(T)}.");

		Children.Add(container, added);
		foreach (var participant in added)
			_known.Add(participant);
	}

	internal void Detach(Container container)
	{
		if (Children.Remove(container, out var removed))
			foreach (var participant in removed)
				_known.Remove(participant);
	}
}
