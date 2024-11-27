using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace JabbR.Middleware
{
    public class DetectSchemeHandler
    {
        private readonly RequestDelegate _next;

        public DetectSchemeHandler(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // This header is set on app harbor since ssl is terminated at the load balancer
            var scheme = context.Request.Headers["X-Forwarded-Proto"].ToString();

            if (!string.IsNullOrEmpty(scheme))
            {
                context.Request.Scheme = scheme;
            }

            await _next(context);
        }
    }
}