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
using System.Security.Claims;
using Microsoft.Extensions.Logging;

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
        private readonly ILogger<PresenceMonitor> _logger;

        public PresenceMonitor(IKernel kernel,
                               IHubContext<Chat> hubContext,
                               ILogger<PresenceMonitor> logger)
        {
            _kernel = kernel;
            _hubContext = hubContext;
            _clients = hubContext.Clients;
            _logger = logger;
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

            try
            {
                _logger.LogInformation("Checking user presence");

using (var repo = _kernel.Get<IJabbrRepository>())
                {
                    // Update the connection presence
                    UpdatePresence(repo);

                    // Remove zombie connections
                    RemoveZombies(repo);

                    // Remove users with no connections
                    RemoveOfflineUsers(repo);

                    // Check the user status
                    CheckUserStatus(repo);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during presence check");
            }
            finally
            {
                _running = false;
            }
        }

        private void UpdatePresence(IJabbrRepository repo)
        {
            // Since we can't directly access all connections, we'll update based on the repository
            var clients = repo.GetAllClients();
            foreach (var client in clients)
            {
                client.LastActivity = DateTimeOffset.UtcNow;
            }

            repo.CommitChanges();
        }

        private void EnsureClientConnected(IJabbrRepository repo, string connectionId, ClaimsPrincipal user)
        {
            if (string.IsNullOrEmpty(connectionId) || user == null)
            {
                return;
            }

            _logger.LogInformation("Connection {ConnectionId} exists but isn't tracked.", connectionId);

            string userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("Unable to find user id for connection {ConnectionId}", connectionId);
                return;
            }

            ChatUser chatUser = repo.GetUserById(userId);
            if (chatUser == null)
            {
                _logger.LogWarning("Unable to find user with id {UserId}", userId);
                return;
            }

            var client = new ChatClient
            {
                Id = connectionId,
                User = chatUser,
                UserAgent = "Unknown", // We can't access HttpContext here, so we set a default value
                LastActivity = DateTimeOffset.UtcNow,
                LastClientActivity = chatUser.LastActivity
            };

            repo.Add(client);
            repo.CommitChanges();
        }

        private void RemoveZombies(IJabbrRepository repo)
        {
            // Remove all zombie clients
            var zombies = repo.Clients.Where(c =>
                SqlFunctions.DateDiff("mi", c.LastActivity, DateTimeOffset.UtcNow) > 3);

            // We're doing to list since there's no MARS support on azure
            foreach (var client in zombies.ToList())
            {
                _logger.LogInformation("Removed zombie connection {ConnectionId}", client.Id);

                repo.Remove(client);
            }
        }

        private void RemoveOfflineUsers(IJabbrRepository repo)
        {
            var offlineUsers = new List<ChatUser>();
            IQueryable<ChatUser> users = repo.GetOnlineUsers();

            foreach (var user in users.ToList())
            {
                if (user.ConnectedClients.Count == 0)
                {
                    _logger.LogInformation("{UserName} has no clients. Marking as offline", user.Name);

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

        private void CheckUserStatus(IJabbrRepository repo)
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
                    await _hubContext.Clients.Group(roomGroup.Room.Name).SendAsync("markInactive", roomGroup.Users);
                });

                repo.CommitChanges();
            }
        }

        private async void PerformRoomAction(List<ChatUser> users, Func<RoomGroup, Task> callback)
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
                    _logger.LogError(ex, "Error occurred during room action");
                }
            }
        }

        private class RoomGroup
        {
            public ChatRoom Room { get; set; }
            public IEnumerable<UserViewModel> Users { get; set; }
        }
    }
}