using System;
using System.Collections.Generic;
using System.Data.Entity.SqlServer;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JabbR.Infrastructure;
using JabbR.Models;
using JabbR.ViewModels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR.Protocol;
using Newtonsoft.Json;
using Ninject;
using Microsoft.AspNetCore.SignalR.Internal;

namespace JabbR.Services
{
    public class PresenceMonitor
    {
        private volatile bool _running;
        private Timer _timer;
        private readonly TimeSpan _presenceCheckInterval = TimeSpan.FromMinutes(1);

        private readonly IKernel _kernel;
        private readonly IHubContext<Chat> _hubContext;
        private readonly IHubClients _clients;

        public PresenceMonitor(IKernel kernel,
                               IHubContext<Chat> hubContext)
        {
            _kernel = kernel;
            _hubContext = hubContext;
            _clients = hubContext.Clients;
        }

        public void Start()
        {
            // Start the timer
            _timer = new Timer(_ =>
            {
                Check();
            },
            null,
            TimeSpan.Zero,
            _presenceCheckInterval);
        }

        private void Check()
        {
            if (_running)
            {
                return;
            }

            _running = true;

            ILogger logger = null;

            try
            {
                logger = _kernel.Get<ILogger>();

                logger.Log("Checking user presence");

                using (var repo = _kernel.Get<IJabbrRepository>())
                {
                    // Update the connection presence
                    UpdatePresence(logger, repo);

                    // Remove zombie connections
                    RemoveZombies(logger, repo);

                    // Remove users with no connections
                    RemoveOfflineUsers(logger, repo);

                    // Check the user status
                    CheckUserStatus(logger, repo);
                }
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.Log(ex);
                }
            }
            finally
            {
                _running = false;
            }
        }

        private void UpdatePresence(ILogger logger, IJabbrRepository repo)
        {
            // Get all connections on this node and update the activity
            foreach (var connectionId in GetAllActiveConnectionIds())
            {
                ChatClient client = repo.GetClientById(connectionId);

                if (client != null)
                {
                    client.LastActivity = DateTimeOffset.UtcNow;
                }
                else
                {
                    EnsureClientConnected(logger, repo, _hubContext.Clients.Client(connectionId).GetHttpContext());
                }
            }

            repo.CommitChanges();
        }

        private IEnumerable<string> GetAllActiveConnectionIds()
        {
            var hubLifetimeManager = (HubLifetimeManager<Chat>)_hubContext.Groups;
            var connectionStore = (IConnectionStore)hubLifetimeManager.GetType()
                .GetField("_connections", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(hubLifetimeManager);

            return connectionStore?.GetConnections().Select(c => c.ConnectionId) ?? Enumerable.Empty<string>();
        }

        // This is an uber hack to make sure the db is in sync with SignalR
        private void EnsureClientConnected(ILogger logger, IJabbrRepository repo, HttpContext context)
        {
            if (context == null)
            {
                return;
            }

            string connectionData = context.Request.Query["connectionData"];

            if (String.IsNullOrEmpty(connectionData))
            {
                return;
            }

            var hubs = JsonConvert.DeserializeObject<HubConnectionData[]>(connectionData);

            if (hubs.Length != 1)
            {
                return;
            }

            // We only care about the chat hub
            if (!hubs[0].Name.Equals("chat", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            logger.Log("Connection exists but isn't tracked.");

            string userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            ChatUser user = repo.GetUserById(userId);
            if (user == null)
            {
                logger.Log("Unable to find user with id {0}", userId);
                return;
            }

            var client = new ChatClient
            {
                Id = context.Connection.Id,
                User = user,
                UserAgent = context.Request.Headers["User-Agent"],
                LastActivity = DateTimeOffset.UtcNow,
                LastClientActivity = user.LastActivity
            };

            repo.Add(client);
            repo.CommitChanges();
        }

        private static void RemoveZombies(ILogger logger, IJabbrRepository repo)
        {
            // Remove all zombie clients 
            var zombies = repo.Clients.Where(c =>
                SqlFunctions.DateDiff("mi", c.LastActivity, DateTimeOffset.UtcNow) > 3);

            // We're doing to list since there's no MARS support on azure
            foreach (var client in zombies.ToList())
            {
                logger.Log("Removed zombie connection {0}", client.Id);

                repo.Remove(client);
            }
        }

        private void RemoveOfflineUsers(ILogger logger, IJabbrRepository repo)
        {
            var offlineUsers = new List<ChatUser>();
            IQueryable<ChatUser> users = repo.GetOnlineUsers();

            foreach (var user in users.ToList())
            {
                if (user.ConnectedClients.Count == 0)
                {
                    logger.Log("{0} has no clients. Marking as offline", user.Name);

                    // Fix users that are marked as inactive but have no clients
                    user.Status = (int)UserStatus.Offline;
                    offlineUsers.Add(user);
                }
            }

            if (offlineUsers.Count > 0)
            {
                PerformRoomAction(offlineUsers, async roomGroup =>
                {
                    foreach (var user in roomGroup.Users)
                    {
                        await _hubContext.Clients.Group(roomGroup.Room.Name).leave(user, roomGroup.Room.Name);
                    }
                });

                repo.CommitChanges();
            }
        }

        private void CheckUserStatus(ILogger logger, IJabbrRepository repo)
        {
            var inactiveUsers = new List<ChatUser>();

            IQueryable<ChatUser> users = repo.GetOnlineUsers().Where(u =>
                SqlFunctions.DateDiff("mi", u.LastActivity, DateTime.UtcNow) > 5);

            foreach (var user in users.ToList())
            {
                user.Status = (int)UserStatus.Inactive;
                inactiveUsers.Add(user);
            }

            if (inactiveUsers.Count > 0)
            {
                PerformRoomAction(inactiveUsers, async roomGroup =>
                {
                    await _hubContext.Clients.Group(roomGroup.Room.Name).markInactive(roomGroup.Users);
                });

                repo.CommitChanges();
            }
        }

        private static async void PerformRoomAction(List<ChatUser> users, Func<RoomGroup, Task> callback)
        {
            var roomGroups = from u in users
                             from r in u.Rooms
                             select new { User = u, Room = r } into tuple
                             group tuple by tuple.Room into g
                             select new RoomGroup
                             {
                                 Room = g.Key,
                                 Users = g.Select(t => new UserViewModel(t.User))
                             };

            foreach (var roomGroup in roomGroups)
            {
                try
                {
                    await callback(roomGroup);
                }
                catch (Exception ex)
                {
                    Trace.TraceError("Error occurred: " + ex);
                }
            }
        }

        private class RoomGroup
        {
            public ChatRoom Room { get; set; }
            public IEnumerable<UserViewModel> Users { get; set; }
        }

        private class HubConnectionData
        {
            public string Name { get; set; }
        }
    }
}