using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pixora.Core.Http;
using Pixora.Core.Services;
using Pixora.Core.Settings;

namespace Pixora.Core.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers Core engine services (settings, http, client, downloader) as singletons.</summary>
    public static IServiceCollection AddPixivCore(this IServiceCollection services)
    {
        // Explicit factory so MS DI doesn't try to resolve the optional `string?` parameter.
        services.TryAddSingleton(_ => new SettingsService());
        services.TryAddSingleton<PixivHttpClientFactory>();
        services.TryAddSingleton<PixivClient>();
        services.TryAddSingleton<ImageResizeService>();
        services.TryAddSingleton<PixivDownloadService>();
        services.TryAddSingleton<PixivImageLoader>();
        return services;
    }
}
