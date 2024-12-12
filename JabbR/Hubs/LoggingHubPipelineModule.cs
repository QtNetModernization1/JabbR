using System;
using System.Threading.Tasks;
using JabbR.Infrastructure;
using Microsoft.AspNetCore.SignalR;

namespace JabbR.Hubs
{
    public class LoggingHubFilter : IHubFilter
    {
        private readonly ILogger _logger;

        public LoggingHubFilter(ILogger logger)
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
                _logger.Log(ex);
                throw;
            }
        }
    }
}