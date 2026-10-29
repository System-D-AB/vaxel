using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vaxel;

namespace Microsoft.Extensions.DependencyInjection;

public static class VaxelServiceCollectionExtensions
{
    /// <summary>
    /// Registers the fragment composer and vaxel core services. Later packets extend this method; do not add a second registration API.
    /// Does not register interactive Blazor or middleware.
    /// </summary>
    public static IServiceCollection AddVaxel(this IServiceCollection services, Action<VaxelOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<VaxelOptions>();
        }

        services.AddHttpContextAccessor();
        services.AddRazorComponents();
        services.TryAddSingleton<IFragmentComposerFactory, FragmentComposerFactory>();
        services.TryAddScoped<ISignalReader, SignalReader>();
        services.TryAddScoped<IFragmentComposer>(sp =>
        {
            var factory = sp.GetRequiredService<IFragmentComposerFactory>();
            var http = sp.GetRequiredService<IHttpContextAccessor>().HttpContext;
            if (http is null)
            {
                throw new VaxelFragmentContextException("HttpContext");
            }

            return factory.Create(http);
        });

        // Push / SSE services
        services.TryAddSingleton<IVaxelScheduler>(SystemScheduler.Instance);
        services.TryAddSingleton<IPushBackplane, InProcessPushBackplane>();
        services.TryAddSingleton<PushConnectionRegistry>();
        services.TryAddSingleton<IPushChannel, PushChannel>();
        services.TryAddSingleton(typeof(Bus<>));
        services.TryAddSingleton(typeof(IBus<>), typeof(Bus<>));

        return services;
    }

    /// <summary>
    /// Registers a singleton <see cref="IBus{T}"/> and <see cref="Bus{T}"/> with an explicit subscriber channel capacity and overflow drop policy.
    /// </summary>
    public static IServiceCollection AddVaxelBus<T>(
        this IServiceCollection services,
        int capacity = 256,
        BusDropPolicy dropPolicy = BusDropPolicy.DropOldest)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(sp => new Bus<T>(sp.GetService<IPushBackplane>(), capacity, dropPolicy));
        services.AddSingleton<IBus<T>>(sp => sp.GetRequiredService<Bus<T>>());
        return services;
    }
}
