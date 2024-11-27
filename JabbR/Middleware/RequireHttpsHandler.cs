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

        public async Task Invoke(HttpContext context)
        {
            var request = context.Request;
            var response = context.Response;

            if (!request.IsHttps)
            {
                var builder = new UriBuilder(request.Scheme, request.Host.Host, request.Host.Port ?? -1, request.Path, request.QueryString.ToString());
                builder.Scheme = "https";

                if (builder.Port == 80)
                {
                    builder.Port = -1;
                }

                response.Redirect(builder.ToString(), true);
                return;
            }

            await _next(context);
        }
    }
}