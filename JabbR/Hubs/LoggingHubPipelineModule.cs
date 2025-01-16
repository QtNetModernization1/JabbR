using System;
using System.Threading.Tasks;
using JabbR.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace JabbR.Hubs
{
    public class LoggingHubFilter : IHubFilter
    {
        private readonly ILogger<LoggingHubFilter> _logger;

        public LoggingHubFilter(ILogger<LoggingHubFilter> logger)
        {
            _logger = logger;
        }

        public async ValueTask<object> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object>> next)
        {
            try
            {
                return await next(invocationContext);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{UserId}: Failure while invoking '{MethodName}'.",
                    invocationContext.Context.User?.Identity?.Name ?? "Unknown",
                    invocationContext.HubMethodName);
                throw;
            }
        }
    }
}