using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace JabbR.Middleware
{
    public class RequireHttpsHandler
    {
        private readonly RequestDelegate _next;

        public RequireHttpsHandler(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!context.Request.IsHttps)
            {
                var host = context.Request.Host;
                var builder = new UriBuilder("https", host.Host);
                if (host.Port.HasValue)
                {
                    builder.Port = host.Port.Value;
                }
                builder.Path = context.Request.Path;
                builder.Query = context.Request.QueryString.ToString();

                context.Response.StatusCode = StatusCodes.Status307TemporaryRedirect;
                context.Response.Headers["Location"] = builder.ToString();
            }
            else
            {
                await _next(context);
            }
        }
    }
}