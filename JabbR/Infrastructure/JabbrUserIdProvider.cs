using Microsoft.AspNetCore.SignalR;

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

            return connection.User.Identity.Name;
        }
    }
}