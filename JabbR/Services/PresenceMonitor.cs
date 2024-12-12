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
using Newtonsoft.Json;
using Ninject;

namespace JabbR.Services
{
    public class PresenceMonitor
    {
        private volatile bool _running;
        private Timer _timer;
        private readonly TimeSpan _presenceCheckInterval = TimeSpan.FromMinutes(1);

        private readonly IKernel _kernel;
        private readonly IHubContext<Chat> _hubContext;

        public PresenceMonitor(IKernel kernel,
                               IHubContext<Chat> hubContext)
        {
            _kernel = kernel;
            _hubContext = hubContext;
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
            // In ASP.NET Core SignalR, we don't have direct access to connections like this.
            // You might need to implement a custom tracking mechanism or use SignalR's built-in features.
            // For now, we'll leave this method empty as a placeholder.
            logger.Log("UpdatePresence method needs to be reimplemented for ASP.NET Core SignalR");
        }

        // This method needs to be reimplemented for ASP.NET Core SignalR
        private void EnsureClientConnected(ILogger logger, IJabbrRepository repo, string connectionId)
        {
            logger.Log("EnsureClientConnected method needs to be reimplemented for ASP.NET Core SignalR");
            // Implementation will depend on how you're tracking connections in ASP.NET Core SignalR
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
                        await _hubContext.Clients.Group(roomGroup.Room.Name).SendAsync("leave", user, roomGroup.Room.Name);
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