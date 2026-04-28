using DDCRM.Shared.Middleware;
using Microsoft.AspNetCore.Builder;

namespace DDCRM.Shared.Extensions;

public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder UseDdcrmCommonPipeline(this IApplicationBuilder app)
    {
        app.UseMiddleware<RequestIdMiddleware>();
        app.UseMiddleware<ApiExceptionMiddleware>();
        return app;
    }
}
