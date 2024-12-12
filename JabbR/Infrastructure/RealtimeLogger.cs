using System;
using System.Diagnostics;
using System.Threading.Tasks;
using JabbR.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace JabbR.Infrastructure
{
    public class RealtimeLogger : ILogger
    {
private readonly IHubContext<Monitor> _logContext;

public RealtimeLogger(IHubContext<Monitor> hubContext)
{
    _logContext = hubContext;
}

        public void Log(LogType type, string message)
        {
            // Fire and forget
            Task.Run(async () =>
            {
                var formatted = String.Format("[{0}]: {1}", DateTime.UtcNow, message);

                try
                {
                    switch (type)
                    {
                        case LogType.Message:
                            await _logContext.Clients.All.SendAsync("logMessage", formatted);
                            break;
                        case LogType.Error:
                            await _logContext.Clients.All.SendAsync("logError", formatted);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceError("Error occurred while logging: " + ex);
                }
            });
        }
    }
}