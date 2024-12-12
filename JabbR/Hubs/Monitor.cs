using JabbR.Infrastructure;
using Microsoft.AspNetCore.SignalR;

namespace JabbR.Hubs
{
    [AuthorizeClaim(JabbRClaimTypes.Admin)]
    public class Monitor : Hub
    {
    }
}