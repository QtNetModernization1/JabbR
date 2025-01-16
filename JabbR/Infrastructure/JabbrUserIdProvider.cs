using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace JabbR.Infrastructure
{
    public class JabbrUserIdProvider : IUserIdProvider
    {
        public string GetUserId(HubConnectionContext connection)
        {
            if (connection.User == null)
            {
                return null;
            }

            return connection.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }
    }
}