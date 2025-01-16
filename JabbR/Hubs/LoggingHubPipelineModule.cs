using System;
using System.Threading.Tasks;
using JabbR.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace JabbR.Hubs
{
    public class LoggingHubPipelineModule : HubPipelineModule
    {
        private readonly ILogger<LoggingHubPipelineModule> _logger;

        public LoggingHubPipelineModule(ILogger<LoggingHubPipelineModule> logger)
        {
            _logger = logger;
        }

        public override Task OnIncomingError(ExceptionContext exceptionContext, IHubIncomingInvokerContext context)
        {
            _logger.LogError(exceptionContext.Error, "{UserId}: Failure while invoking '{MethodName}'.",
                context.Hub.Context.User?.Identity?.Name ?? "Unknown",
                context.MethodDescriptor.Name);

            return base.OnIncomingError(exceptionContext, context);
        }
    }
}