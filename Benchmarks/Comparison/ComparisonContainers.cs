using GroveGames.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using GroveBuilder = GroveGames.DependencyInjection.ContainerBuilder;
using GroveContainer = GroveGames.DependencyInjection.IContainer;

namespace Injectra.Benchmarks.Comparison;

internal static class ComparisonContainers
{
	internal static Container BuildInjectra()
	{
		var builder = new ContainerBuilder();
		builder.Register<Clock>(Lifetime.Singleton).As<IClock>();
		builder.Register<Enemy>(Lifetime.Transient);
		builder.Register<Movement>(Lifetime.Transient);
		builder.Register<Player>(Lifetime.Transient);
		return builder.Build();
	}

	internal static ServiceProvider BuildMicrosoft()
	{
		var services = new ServiceCollection();
		services.AddSingleton<IClock, Clock>();
		services.AddTransient<Enemy>();
		services.AddTransient<Movement>();
		services.AddTransient<Player>();
		// Match build-time graph validation; initialization remains outside warm resolve measurements.
		return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
	}

	internal static GroveContainer BuildGrove()
	{
		var builder = new GroveBuilder();
		builder.AddSingleton<IClock, Clock>();
		builder.AddTransient<Enemy>();
		builder.AddTransient<Movement>();
		builder.AddTransient<Player>();
		return builder.Build();
	}

	internal static Container BuildGenericInjectra()
	{
		var builder = new ContainerBuilder();
		builder.Register(typeof(Repository<>), Lifetime.Singleton).As(typeof(IRepository<>));
		builder.Register(typeof(GenericConsumer<>), Lifetime.Transient);
		return builder.Build();
	}

	internal static ServiceProvider BuildGenericMicrosoft()
	{
		var services = new ServiceCollection();
		services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
		services.AddTransient(typeof(GenericConsumer<>));
		return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
	}
}
