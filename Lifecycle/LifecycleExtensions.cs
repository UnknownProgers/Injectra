namespace Injectra.Lifecycle;

/// <summary>Connects lifecycle dispatch to registrations in an Injectra root container.</summary>
public static class LifecycleExtensions
{
	/// <summary>Registers one eagerly created, container-owned lifecycle manager.</summary>
	/// <param name="builder">The root container builder.</param>
	/// <param name="configure">Optional phase configuration, invoked during Build after participants can be resolved.</param>
	/// <returns>The same builder.</returns>
	/// <exception cref="ArgumentNullException">The builder is null.</exception>
	/// <exception cref="InvalidOperationException">The builder is complete.</exception>
	/// <remarks>
	/// Build creates participants through ResolveAll for the explicitly registered lifecycle contracts.
	/// Participating registrations must be singleton or instance registrations. Initial Initialize remains explicit.
	/// Custom phases must be registered inside configure. Children automatically join the same manager; attachment requested during callbacks is deferred until the traversal ends.
	/// Repeated calls cause Build to reject the duplicate manager registration. Configuration exceptions propagate from Build.
	/// </remarks>
	public static ContainerBuilder UseLifecycle(this ContainerBuilder builder, Action<InjectraLifecycle>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(builder);
		builder.Register<InjectraLifecycle>(resolver =>
		{
			var lifecycle = new InjectraLifecycle((Container)resolver);
			try
			{
				configure?.Invoke(lifecycle);
				lifecycle.SealConfiguration();
				return lifecycle;
			}
			catch
			{
				lifecycle.Dispose();
				throw;
			}
		}, Lifetime.Singleton).NonLazy();
		return builder;
	}
}
