using System;
using System.Threading.Tasks;
using JabbR.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace JabbR.Hubs
{
    public static class LoggingHubExtensions
    {
        public static IHubPipelineBuilder UseLogging(this IHubPipelineBuilder builder, ILogger logger)
        {
            return builder.Use(async (context, next) =>
            {
                try
                {
                    await next();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "{UserId}: Failure while invoking '{MethodName}'.",
                        context.Context.User?.Identity?.Name ?? "Unknown",
                        context.HubMethodName);
                    throw;
                }
            });
        }
    }
}