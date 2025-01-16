using JabbR.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;

namespace JabbR.Hubs
{
    [Authorize(Policy = "AdminPolicy")]
    public class Monitor : Hub
    {
    }
}