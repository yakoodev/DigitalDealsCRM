using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;

namespace DDCRM.Shared.Auth;

public static class ServiceTokenAuthExtensions
{
    public static IServiceCollection AddServiceTokenAuth(
        this IServiceCollection services,
        Action<ServiceTokenAuthOptions> configure)
    {
        services.Configure(configure);
        return services;
    }

    public static IApplicationBuilder UseServiceTokenAuth(this IApplicationBuilder app)
    {
        app.UseMiddleware<ServiceTokenAuthMiddleware>();
        return app;
    }
}
