using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace JabbR.Middleware
{
    public class DetectSchemeHandler : IMiddleware
    {
        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            // This header is set on app harbor since ssl is terminated at the load balancer
            var scheme = context.Request.Headers["X-Forwarded-Proto"].ToString();

            if (!string.IsNullOrEmpty(scheme))
            {
                context.Request.Scheme = scheme;
            }

            await next(context);
        }
    }
}