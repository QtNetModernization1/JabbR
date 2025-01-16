using System;
using JabbR.Infrastructure;
using Microsoft.AspNetCore.SignalR;

namespace JabbR.Hubs
{
public class LoggingHubPipelineModule : IHubFilter
    {
        private readonly ILogger _logger;

        public LoggingHubPipelineModule(ILogger logger)
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
                _logger.LogError("{0}: Failure while invoking '{1}'.", invocationContext.Context.User.Identity.Name, invocationContext.HubMethodName);
                _logger.LogError(ex, "Error details");
                throw;
            }
        }
    }
}