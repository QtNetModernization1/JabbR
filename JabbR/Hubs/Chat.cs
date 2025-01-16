using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JabbR.Commands;
using JabbR.ContentProviders.Core;
using JabbR.Infrastructure;
using JabbR.Models;
using JabbR.Services;
using JabbR.ViewModels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json;

namespace JabbR
{
    [Authorize]
    public class Chat : Hub, INotificationService
    {
        private static readonly TimeSpan _disconnectThreshold = TimeSpan.FromSeconds(10);

        private readonly IJabbrRepository _repository;
        private readonly IChatService _service;
        private readonly IRecentMessageCache _recentMessageCache;
        private readonly ICache _cache;
        private readonly ContentProviderProcessor _resourceProcessor;
        private readonly ILogger _logger;
        private readonly ApplicationSettings _settings;

        public Chat(ContentProviderProcessor resourceProcessor,
                    IChatService service,
                    IRecentMessageCache recentMessageCache,
                    IJabbrRepository repository,
                    ICache cache,
                    ILogger logger,
                    ApplicationSettings settings)
        {
            _resourceProcessor = resourceProcessor;
            _service = service;
            _recentMessageCache = recentMessageCache;
            _repository = repository;
            _cache = cache;
            _logger = logger;
            _settings = settings;
        }

        private string UserAgent
        {
            get
            {
                var httpContext = Context.GetHttpContext();
                if (httpContext != null && httpContext.Request.Headers.TryGetValue("User-Agent", out var userAgent))
                {
                    return userAgent;
                }
                return null;
            }
        }

        private bool OutOfSync
        {
            get
            {
                var httpContext = Context.GetHttpContext();
                string version = httpContext?.Request.Query["version"];

                if (String.IsNullOrEmpty(version))
                {
                    return true;
                }

                return new Version(version) != Constants.JabbRVersion;
            }
        }

        public override async Task OnConnectedAsync()
        {
            _logger.Log("OnConnected({0})", Context.ConnectionId);

            await CheckStatus();

            await base.OnConnectedAsync();
        }

        public void Join()
        {
            Join(reconnecting: false);
        }

        public void Join(bool reconnecting)
        {
            // Get the client state
            var userId = Context.User.GetUserId();

            // Try to get the user from the client state
            ChatUser user = _repository.GetUserById(userId);

            if (reconnecting)
            {
                _logger.Log("{0}:{1} connected after dropping connection.", user.Name, Context.ConnectionId);

                // If the user was marked as offline then mark them inactive
                if (user.Status == (int)UserStatus.Offline)
                {
                    user.Status = (int)UserStatus.Inactive;
                    _repository.CommitChanges();
                }

                // Ensure the client is re-added
                _service.AddClient(user, Context.ConnectionId, UserAgent);
            }
            else
            {
                _logger.Log("{0}:{1} connected.", user.Name, Context.ConnectionId);

                // Update some user values
                _service.UpdateActivity(user, Context.ConnectionId, UserAgent);
                _repository.CommitChanges();
            }

            ClientState clientState = GetClientState();

            OnUserInitialize(clientState, user, reconnecting);
        }

        private async Task CheckStatus()
        {
            if (OutOfSync)
            {
                await Clients.Caller.SendAsync("outOfSync");
            }
        }

        private void OnUserInitialize(ClientState clientState, ChatUser user, bool reconnecting)
        {
            // Update the active room on the client (only if it's still a valid room)
            if (user.Rooms.Any(room => room.Name.Equals(clientState.ActiveRoom, StringComparison.OrdinalIgnoreCase)))
            {
                // Update the active room on the client (only if it's still a valid room)
                Clients.Caller.SendAsync("setActiveRoom", clientState.ActiveRoom).Wait();
            }

            LogOn(user, Context.ConnectionId, reconnecting);
        }

        public Task<bool> Send(string content, string roomName)
        {
            var message = new ClientMessage
            {
                Content = content,
                Room = roomName
            };

            return Send(message);
        }

        public async Task<bool> Send(ClientMessage clientMessage)
        {
            await CheckStatus();

            // reject it if it's too long
            if (_settings.MaxMessageLength > 0 && clientMessage.Content.Length > _settings.MaxMessageLength)
            {
                throw new HubException(String.Format(LanguageResources.SendMessageTooLong, _settings.MaxMessageLength));
            }

            // See if this is a valid command (starts with /)
            if (TryHandleCommand(clientMessage.Content, clientMessage.Room))
            {
                return true;
            }

            var userId = Context.User.GetUserId();

            ChatUser user = _repository.VerifyUserId(userId);
            ChatRoom room = _repository.VerifyUserRoom(_cache, user, clientMessage.Room);

            if (room == null || (room.Private && !user.AllowedRooms.Contains(room)))
            {
                return false;
            }

            // REVIEW: Is it better to use the extension method room.EnsureOpen here?
            if (room.Closed)
            {
                throw new HubException(String.Format(LanguageResources.SendMessageRoomClosed, clientMessage.Room));
            }

            // Update activity *after* ensuring the user, this forces them to be active
            await UpdateActivity(user, room);

            // Create a true unique id and save the message to the db
            string id = Guid.NewGuid().ToString("d");
            ChatMessage chatMessage = _service.AddMessage(user, room, id, clientMessage.Content);
            _repository.CommitChanges();


            var messageViewModel = new MessageViewModel(chatMessage);

            if (clientMessage.Id == null)
            {
                // If the client didn't generate an id for the message then just
                // send it to everyone. The assumption is that the client has some ui
                // that it wanted to update immediately showing the message and
                // then when the actual message is roundtripped it would "solidify it".
                await Clients.Group(room.Name).SendAsync("addMessage", messageViewModel, room.Name);
            }
            else
            {
                // If the client did set an id then we need to give everyone the real id first
                await Clients.OthersInGroup(room.Name).SendAsync("addMessage", messageViewModel, room.Name);

                // Now tell the caller to replace the message
                await Clients.Caller.SendAsync("replaceMessage", clientMessage.Id, messageViewModel, room.Name);
            }

            // Add mentions
            await AddMentions(chatMessage);

            var urls = UrlExtractor.ExtractUrls(chatMessage.Content);
            if (urls.Count > 0)
            {
                _resourceProcessor.ProcessUrls(urls, room.Name, chatMessage.Id);
            }

            return true;
        }

        private async Task AddMentions(ChatMessage message)
        {
            var mentionedUsers = new List<ChatUser>();
            foreach (var userName in MentionExtractor.ExtractMentions(message.Content))
            {
                ChatUser mentionedUser = _repository.GetUserByName(userName);

                // Don't create a mention if
                // 1. If the mentioned user doesn't exist.
                // 2. If you mention yourself
                // 3. If you're mentioned in a private room that you don't have access to
                // 4. You've already been mentioned in this message
                if (mentionedUser == null ||
                    mentionedUser == message.User ||
                    (message.Room.Private && !mentionedUser.AllowedRooms.Contains(message.Room)) ||
                    mentionedUsers.Contains(mentionedUser))
                {
                    continue;
                }

                // mark as read if ALL of the following
                // 1. user is not offline
                // 2. user is not AFK
                // 3. user has been active within the last 10 minutes
                // 4. user is currently in the room
                bool markAsRead = mentionedUser.Status != (int)UserStatus.Offline
                                  && !mentionedUser.IsAfk
                                  && (DateTimeOffset.UtcNow - mentionedUser.LastActivity) < TimeSpan.FromMinutes(10)
                                  && _repository.IsUserInRoom(_cache, mentionedUser, message.Room);

                _service.AddNotification(mentionedUser, message, message.Room, markAsRead);

                mentionedUsers.Add(mentionedUser);
            }

            if (mentionedUsers.Count > 0)
            {
                _repository.CommitChanges();
            }

            foreach (var user in mentionedUsers)
            {
                await UpdateUnreadMentions(user);
            }
        }

        private async Task UpdateUnreadMentions(ChatUser mentionedUser)
        {
            var unread = _repository.GetUnreadNotificationsCount(mentionedUser);

            await Clients.User(mentionedUser.Id)
                   .SendAsync("updateUnreadNotifications", unread);
        }

        public UserViewModel GetUserInfo()
        {
            var userId = Context.User.GetUserId();

            ChatUser user = _repository.VerifyUserId(userId);

            return new UserViewModel(user);
        }

        // This method is called when a client reconnects after a disconnection
        public async Task HandleReconnection()
        {
            _logger.Log("HandleReconnection({0})", Context.ConnectionId);

            var userId = Context.User.GetUserId();

            ChatUser user = _repository.VerifyUserId(userId);

            if (user == null)
            {
                _logger.Log("Reconnect failed user {0}:{1} doesn't exist.", userId, Context.ConnectionId);
                return;
            }

            // Make sure this client is being tracked
            _service.AddClient(user, Context.ConnectionId, UserAgent);

            var currentStatus = (UserStatus)user.Status;

            if (currentStatus == UserStatus.Offline)
            {
                _logger.Log("{0}:{1} reconnected after temporary network problem and marked offline.", user.Name, Context.ConnectionId);

                // Mark the user as inactive
                user.Status = (int)UserStatus.Inactive;
                _repository.CommitChanges();

                // If the user was offline that means they are not in the user list so we need to tell
                // everyone the user is really in the room
                var userViewModel = new UserViewModel(user);

                foreach (var room in user.Rooms)
                {
                    var isOwner = user.OwnedRooms.Contains(room);

                    // Tell the people in this room that you've joined
                    await Clients.Group(room.Name).SendAsync("addUser", userViewModel, room.Name, isOwner);
                }
            }
            else
            {
                _logger.Log("{0}:{1} reconnected after temporary network problem.", user.Name, Context.ConnectionId);
            }

            await CheckStatus();
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            _logger.Log("OnDisconnected({0})", Context.ConnectionId);

            DisconnectClient(Context.ConnectionId, useThreshold: true);

            return base.OnDisconnectedAsync(exception);
        }

        public object GetCommands()
        {
            return CommandManager.GetCommandsMetaData();
        }

        public object GetShortcuts()
        {
            return new[] {
                new { Name = "Tab or Shift + Tab", Group = "shortcut", IsKeyCombination = true, Description = LanguageResources.Client_ShortcutTabs },
                new { Name = "Alt + L", Group = "shortcut", IsKeyCombination = true, Description = LanguageResources.Client_ShortcutLobby },
                new { Name = "Alt + Number", Group = "shortcut", IsKeyCombination = true, Description = LanguageResources.Client_ShortcutSpecificTab }
            };
        }

        public Task<List<LobbyRoomViewModel>> GetRooms()
        {
            string userId = Context.User.GetUserId();
            ChatUser user = _repository.VerifyUserId(userId);

            return _repository.GetAllowedRooms(user).Select(r => new LobbyRoomViewModel
            {
                Name = r.Name,
                Count = r.Users.Count(u => u.Status != (int)UserStatus.Offline),
                Private = r.Private,
                Closed = r.Closed,
                Topic = r.Topic
            }).ToListAsync();
        }

        public async Task<IEnumerable<MessageViewModel>> GetPreviousMessages(string messageId)
        {
            var previousMessages = await (from m in _repository.GetPreviousMessages(messageId)
                                          orderby m.When descending
                                          select m).Take(100).ToListAsync();


            return previousMessages.AsEnumerable()
                                   .Reverse()
                                   .Select(m => new MessageViewModel(m));
        }

        public async Task LoadRooms(string[] roomNames)
        {
            string userId = Context.User.GetUserId();
            ChatUser user = _repository.VerifyUserId(userId);

            var rooms = await _repository.Rooms.Where(r => roomNames.Contains(r.Name))
                                               .ToListAsync();

            // Can't async whenall because we'd be hitting a single 
            // EF context with multiple concurrent queries.
            foreach (var room in rooms)
            {
                if (room == null || (room.Private && !user.AllowedRooms.Contains(room)))
                {
                    continue;
                }

                RoomViewModel roomInfo = null;

                while (true)
                {
                    try
                    {
                        // If invoking roomLoaded fails don't get the roomInfo again
                        roomInfo = roomInfo ?? await GetRoomInfoCore(room);
                        await Clients.Caller.SendAsync("roomLoaded", roomInfo);
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.Log(ex);
                    }
                }
            }
        }

        public Task<RoomViewModel> GetRoomInfo(string roomName)
        {
            if (String.IsNullOrEmpty(roomName))
            {
                return null;
            }

            string userId = Context.User.GetUserId();
            ChatUser user = _repository.VerifyUserId(userId);

            ChatRoom room = _repository.GetRoomByName(roomName);

            if (room == null || (room.Private && !user.AllowedRooms.Contains(room)))
            {
                return null;
            }

            return GetRoomInfoCore(room);
        }

        private async Task<RoomViewModel> GetRoomInfoCore(ChatRoom room)
        {
            var recentMessages = _recentMessageCache.GetRecentMessages(room.Name);

            // If we haven't cached enough messages just populate it now
            if (recentMessages.Count == 0)
            {
                var messages = await (from m in _repository.GetMessagesByRoom(room)
                                      orderby m.When descending
                                      select m).Take(50).ToListAsync();
                // Reverse them since we want to get them in chronological order
                messages.Reverse();

                recentMessages = messages.Select(m => new MessageViewModel(m)).ToList();

                _recentMessageCache.Add(room.Name, recentMessages);
            }

            // Get online users through the repository
            List<ChatUser> onlineUsers = await _repository.GetOnlineUsers(room).ToListAsync();

            return new RoomViewModel
            {
                Name = room.Name,
                Users = from u in onlineUsers
                        select new UserViewModel(u),
                Owners = from u in room.Owners.Online()
                         select u.Name,
                RecentMessages = recentMessages,
                Topic = room.Topic ?? String.Empty,
                Welcome = room.Welcome ?? String.Empty,
                Closed = room.Closed
            };
        }

        public void PostNotification(ClientNotification notification)
        {
            PostNotification(notification, executeContentProviders: true);
        }

        public void PostNotification(ClientNotification notification, bool executeContentProviders)
        {
            string userId = Context.User.GetUserId();

            ChatUser user = _repository.GetUserById(userId);
            ChatRoom room = _repository.VerifyUserRoom(_cache, user, notification.Room);

            // User must be an owner
            if (room == null ||
                !room.Owners.Contains(user) ||
                (room.Private && !user.AllowedRooms.Contains(room)))
            {
                throw new HubException(LanguageResources.PostNotification_NotAllowed);
            }

            var chatMessage = new ChatMessage
            {
                Id = Guid.NewGuid().ToString("d"),
                Content = notification.Content,
                User = user,
                Room = room,
                HtmlEncoded = false,
                ImageUrl = notification.ImageUrl,
                Source = notification.Source,
                When = DateTimeOffset.UtcNow,
                MessageType = (int)MessageType.Notification
            };

            _repository.Add(chatMessage);
            _repository.CommitChanges();

            Clients.Group(room.Name).SendAsync("addMessage", new MessageViewModel(chatMessage), room.Name);

            if (executeContentProviders)
            {
                var urls = UrlExtractor.ExtractUrls(chatMessage.Content);
                if (urls.Count > 0)
                {
                    _resourceProcessor.ProcessUrls(urls, room.Name, chatMessage.Id);
                }
            }
        }

        public async Task Typing(string roomName)
        {
            string userId = Context.User.GetUserId();

            ChatUser user = _repository.GetUserById(userId);
            ChatRoom room = _repository.VerifyUserRoom(_cache, user, roomName);

            if (room == null || (room.Private && !user.AllowedRooms.Contains(room)))
            {
                return;
            }

            await UpdateActivity(user, room);

            var userViewModel = new UserViewModel(user);
            await Clients.Group(room.Name).SendAsync("setTyping", userViewModel, room.Name);
        }

        public async Task UpdateActivity()
        {
            string userId = Context.User.GetUserId();

            ChatUser user = _repository.GetUserById(userId);

            foreach (var room in user.Rooms)
            {
                await UpdateActivity(user, room);
            }

            await CheckStatus();
        }

        public void TabOrderChanged(string[] tabOrdering)
        {
            string userId = Context.User.GetUserId();

            ChatUser user = _repository.GetUserById(userId);

            ChatUserPreferences userPreferences = user.Preferences;
            userPreferences.TabOrder = tabOrdering.ToList();
            user.Preferences = userPreferences;

            _repository.CommitChanges();

            Clients.User(user.Id).SendAsync("updateTabOrder", tabOrdering);
        }

        private void LogOn(ChatUser user, string clientId, bool reconnecting)
        {
            if (!reconnecting)
            {
                // Update the client state
                Clients.Caller.SendAsync("setId", user.Id).Wait();
                Clients.Caller.SendAsync("setName", user.Name).Wait();
                Clients.Caller.SendAsync("setHash", user.Hash).Wait();
                Clients.Caller.SendAsync("setUnreadNotifications", user.Notifications.Count(n => !n.Read)).Wait();
            }

            var rooms = new List<RoomViewModel>();
            var privateRooms = new List<LobbyRoomViewModel>();
            var userViewModel = new UserViewModel(user);
            var ownedRooms = user.OwnedRooms.Select(r => r.Key);

            foreach (var room in user.Rooms)
            {
                var isOwner = ownedRooms.Contains(room.Key);

                // Tell the people in this room that you've joined
                Clients.Group(room.Name).SendAsync("addUser", userViewModel, room.Name, isOwner).Wait();

                // Add the caller to the group so they receive messages
                Groups.AddToGroupAsync(Context.ConnectionId, room.Name).Wait();

                if (!reconnecting)
                {
                    // Add to the list of room names
                    rooms.Add(new RoomViewModel
                    {
                        Name = room.Name,
                        Private = room.Private,
                        Closed = room.Closed
                    });
                }
            }

            if (!reconnecting)
            {
                foreach (var r in user.AllowedRooms)
                {
                    privateRooms.Add(new LobbyRoomViewModel
                    {
                        Name = r.Name,
                        Count = _repository.GetOnlineUsers(r).Count(),
                        Private = r.Private,
                        Closed = r.Closed,
                        Topic = r.Topic
                    });
                }

                // Initialize the chat with the rooms the user is in
                Clients.Caller.SendAsync("logOn", rooms, privateRooms, user.Preferences).Wait();
            }
        }

        private async Task UpdateActivity(ChatUser user, ChatRoom room)
        {
            UpdateActivity(user);

            await OnUpdateActivity(user, room);
        }

        private void UpdateActivity(ChatUser user)
        {
            _service.UpdateActivity(user, Context.ConnectionId, UserAgent);

            _repository.CommitChanges();
        }

        private bool TryHandleCommand(string command, string room)
        {
            string clientId = Context.ConnectionId;
            string userId = Context.User.GetUserId();

            var commandManager = new CommandManager(clientId, UserAgent, userId, room, _service, _repository, _cache, this);
            return commandManager.TryHandleCommand(command);
        }

        private void DisconnectClient(string clientId, bool useThreshold = false)
        {
            string userId = _service.DisconnectClient(clientId);

            if (String.IsNullOrEmpty(userId))
            {
                _logger.Log("Failed to disconnect {0}. No user found", clientId);
                return;
            }

            if (useThreshold)
            {
                Thread.Sleep(_disconnectThreshold);
            }

            // Query for the user to get the updated status
            ChatUser user = _repository.GetUserById(userId);

            // There's no associated user for this client id
            if (user == null)
            {
                _logger.Log("Failed to disconnect {0}:{1}. No user found", userId, clientId);
                return;
            }

            _repository.Reload(user);

            _logger.Log("{0}:{1} disconnected", user.Name, Context.ConnectionId);

            // The user will be marked as offline if all clients leave
            if (user.Status == (int)UserStatus.Offline)
            {
                _logger.Log("Marking {0} offline", user.Name);

                foreach (var room in user.Rooms)
                {
                    var userViewModel = new UserViewModel(user);

                    Clients.OthersInGroup(room.Name).SendAsync("Leave", userViewModel, room.Name);
                }
            }
        }

        private async Task OnUpdateActivity(ChatUser user, ChatRoom room)
        {
            var userViewModel = new UserViewModel(user);
            await Clients.Group(room.Name).SendAsync("updateActivity", userViewModel, room.Name);
        }

        private async Task LeaveRoom(ChatUser user, ChatRoom room)
        {
            var userViewModel = new UserViewModel(user);
            await Clients.Group(room.Name).SendAsync("leave", userViewModel, room.Name);

            foreach (var client in user.ConnectedClients)
            {
                await Groups.RemoveFromGroupAsync(client.Id, room.Name);
            }

            OnRoomChanged(room);
        }

        void INotificationService.LogOn(ChatUser user, string clientId)
        {
            LogOn(user, clientId, reconnecting: true);
        }

        void INotificationService.KickUser(ChatUser targetUser, ChatRoom room, ChatUser callingUser, string reason)
        {
            var targetUserViewModel = new UserViewModel(targetUser);
            var callingUserViewModel = new UserViewModel(callingUser);

            if (String.IsNullOrWhiteSpace(reason))
            {
                reason = null;
            }

            Clients.Group(room.Name).SendAsync("kick", targetUserViewModel, room.Name, callingUserViewModel, reason);

            foreach (var client in targetUser.ConnectedClients)
            {
                Groups.RemoveFromGroupAsync(client.Id, room.Name);
            }

            OnRoomChanged(room);
        }

        async void INotificationService.OnUserCreated(ChatUser user)
        {
            // Set some client state
            await Clients.Caller.SendAsync("setName", user.Name);
            await Clients.Caller.SendAsync("setId", user.Id);
            await Clients.Caller.SendAsync("setHash", user.Hash);

            // Tell the client a user was created
            await Clients.Caller.SendAsync("userCreated");
        }

void INotificationService.JoinRoom(ChatUser user, ChatRoom room)
{
    JoinRoomAsync(user, room).GetAwaiter().GetResult();
}

private async Task JoinRoomAsync(ChatUser user, ChatRoom room)
{
    var userViewModel = new UserViewModel(user);
    var roomViewModel = new RoomViewModel
    {
        Name = room.Name,
        Private = room.Private,
        Welcome = room.Welcome ?? String.Empty,
        Closed = room.Closed
    };

    var isOwner = user.OwnedRooms.Contains(room);

    // Tell all clients to join this room
    await Clients.User(user.Id).SendAsync("joinRoom", roomViewModel);

    // Tell the people in this room that you've joined
    await Clients.Group(room.Name).SendAsync("addUser", userViewModel, room.Name, isOwner);

    // Notify users of the room count change
    await OnRoomChangedAsync(room);

    foreach (var client in user.ConnectedClients)
    {
        await Groups.AddToGroupAsync(client.Id, room.Name);
    }
}

        async void INotificationService.AllowUser(ChatUser targetUser, ChatRoom targetRoom)
        {
            // Build a viewmodel for the room
            var roomViewModel = new RoomViewModel
            {
                Name = targetRoom.Name,
                Private = targetRoom.Private,
                Closed = targetRoom.Closed,
                Topic = targetRoom.Topic ?? String.Empty,
                Count = _repository.GetOnlineUsers(targetRoom).Count()
            };

            // Tell this client it's allowed.  Pass down a viewmodel so that we can add the room to the lobby.
            await Clients.User(targetUser.Id).SendAsync("allowUser", targetRoom.Name, roomViewModel);

            // Tell the calling client the granting permission into the room was successful
            await Clients.Caller.SendAsync("userAllowed", targetUser.Name, targetRoom.Name);
        }

        async void INotificationService.UnallowUser(ChatUser targetUser, ChatRoom targetRoom, ChatUser callingUser)
        {
            // Kick the user from the room when they are unallowed
            ((INotificationService)this).KickUser(targetUser, targetRoom, callingUser, null);

            // Tell this client it's no longer allowed
            await Clients.User(targetUser.Id).SendAsync("unallowUser", targetRoom.Name);

            // Tell the calling client the granting permission into the room was successful
            await Clients.Caller.SendAsync("userUnallowed", targetUser.Name, targetRoom.Name);
        }

        async void INotificationService.AddOwner(ChatUser targetUser, ChatRoom targetRoom)
        {
            // Tell this client it's an owner
            await Clients.User(targetUser.Id).SendAsync("makeOwner", targetRoom.Name);

            var userViewModel = new UserViewModel(targetUser);

            // If the target user is in the target room.
            // Tell everyone in the target room that a new owner was added
            if (_repository.IsUserInRoom(_cache, targetUser, targetRoom))
            {
                await Clients.Group(targetRoom.Name).SendAsync("addOwner", userViewModel, targetRoom.Name);
            }

            // Tell the calling client the granting of ownership was successful
            await Clients.Caller.SendAsync("ownerMade", targetUser.Name, targetRoom.Name);
        }

        async void INotificationService.RemoveOwner(ChatUser targetUser, ChatRoom targetRoom)
        {
            // Tell this client it's no longer an owner
            await Clients.User(targetUser.Id).SendAsync("demoteOwner", targetRoom.Name);

            var userViewModel = new UserViewModel(targetUser);

            // If the target user is in the target room.
            // Tell everyone in the target room that the owner was removed
            if (_repository.IsUserInRoom(_cache, targetUser, targetRoom))
            {
                await Clients.Group(targetRoom.Name).SendAsync("removeOwner", userViewModel, targetRoom.Name);
            }

            // Tell the calling client the removal of ownership was successful
            await Clients.Caller.SendAsync("ownerRemoved", targetUser.Name, targetRoom.Name);
        }

        async void INotificationService.ChangeGravatar(ChatUser user)
        {
            await Clients.Caller.SendAsync("setHash", user.Hash);

            // Update the calling client
            await Clients.User(user.Id).SendAsync("gravatarChanged", user.Hash);

            // Create the view model
            var userViewModel = new UserViewModel(user);

            // Tell all users in rooms to change the gravatar
            foreach (var room in user.Rooms)
            {
                await Clients.Group(room.Name).SendAsync("changeGravatar", userViewModel, room.Name);
            }
        }

        void INotificationService.OnSelfMessage(ChatRoom room, ChatUser user, string content)
        {
            Clients.Group(room.Name).SendAsync("sendMeMessage", user.Name, content, room.Name);
        }

        async void INotificationService.SendPrivateMessage(ChatUser fromUser, ChatUser toUser, string messageText)
        {
            // Send a message to the sender and the sendee
            await Clients.User(fromUser.Id).SendAsync("sendPrivateMessage", fromUser.Name, toUser.Name, messageText);

            await Clients.User(toUser.Id).SendAsync("sendPrivateMessage", fromUser.Name, toUser.Name, messageText);
        }

        async void INotificationService.PostNotification(ChatRoom room, ChatUser user, string message)
        {
            await Clients.User(user.Id).SendAsync("postNotification", message, room.Name);
        }

        async void INotificationService.ListRooms(ChatUser user)
        {
            string userId = Context.User.GetUserId();

            var userModel = new UserViewModel(user);

            await Clients.Caller.SendAsync("showUsersRoomList", userModel, user.Rooms.Allowed(userId).Select(r => r.Name));
        }

        async void INotificationService.ListUsers()
        {
            var users = _repository.Users.Online().Select(s => s.Name).OrderBy(s => s);
            await Clients.Caller.SendAsync("listUsers", users);
        }

        async void INotificationService.ListUsers(IEnumerable<ChatUser> users)
        {
            await Clients.Caller.SendAsync("listUsers", users.Select(s => s.Name));
        }

        void INotificationService.ListUsers(ChatRoom room, IEnumerable<string> names)
        {
            Clients.Caller.SendAsync("showUsersInRoom", room.Name, names).Wait();
        }

        async void INotificationService.ListAllowedUsers(ChatRoom room)
        {
            await Clients.Caller.SendAsync("listAllowedUsers", room.Name, room.Private, room.AllowedUsers.Select(s => s.Name));
        }

        async void INotificationService.ListOwners(ChatRoom room)
        {
            await Clients.Caller.SendAsync("listOwners", room.Name, room.Owners.Select(s => s.Name), room.Creator != null ? room.Creator.Name : null);
        }

        async void INotificationService.LockRoom(ChatUser targetUser, ChatRoom room)
        {
            var userViewModel = new UserViewModel(targetUser);

            // Tell everyone that the room's locked
            await Clients.Clients(_repository.GetAllowedClientIds(room)).SendAsync("lockRoom", userViewModel, room.Name, true);
            await Clients.AllExcept(_repository.GetAllowedClientIds(room).ToArray()).SendAsync("lockRoom", userViewModel, room.Name, false);

            // Tell the caller the room was successfully locked
            await Clients.Caller.SendAsync("roomLocked", room.Name);

            // Notify people of the change
            OnRoomChanged(room);
        }

        async void INotificationService.CloseRoom(IEnumerable<ChatUser> users, ChatRoom room)
        {
            // notify all members of room that it is now closed
            foreach (var user in users)
            {
                await Clients.User(user.Id).SendAsync("roomClosed", room.Name);
            }

            // notify everyone to update their lobby
            OnRoomChanged(room);
        }

        async void INotificationService.UnCloseRoom(IEnumerable<ChatUser> users, ChatRoom room)
        {
            // notify all members of room that it is now re-opened
            foreach (var user in users)
            {
                await Clients.User(user.Id).SendAsync("roomUnClosed", room.Name);
            }

            // notify everyone to update their lobby
            OnRoomChanged(room);
        }

        async void INotificationService.LogOut(ChatUser user, string clientId)
        {
            foreach (var client in user.ConnectedClients)
            {
                DisconnectClient(client.Id);
                await Clients.Client(client.Id).SendAsync("logOut");
            }
        }

        async void INotificationService.ShowUserInfo(ChatUser user)
        {
            string userId = Context.User.GetUserId();

            await Clients.Caller.SendAsync("showUserInfo", new
            {
                Name = user.Name,
                OwnedRooms = user.OwnedRooms
                    .Allowed(userId)
                    .Where(r => !r.Closed)
                    .Select(r => r.Name),
                Status = ((UserStatus)user.Status).ToString(),
                LastActivity = user.LastActivity,
                IsAfk = user.IsAfk,
                AfkNote = user.AfkNote,
                Note = user.Note,
                Hash = user.Hash,
                Rooms = user.Rooms.Allowed(userId).Select(r => r.Name)
            });
        }

        async void INotificationService.ShowHelp()
        {
            await Clients.Caller.SendAsync("showCommands");
        }

        async void INotificationService.Invite(ChatUser user, ChatUser targetUser, ChatRoom targetRoom)
        {

            // Send the invite message to the sendee
            await Clients.User(targetUser.Id).SendAsync("sendInvite", user.Name, targetUser.Name, targetRoom.Name);

            // Send the invite notification to the sender
            await Clients.User(user.Id).SendAsync("sendInvite", user.Name, targetUser.Name, targetRoom.Name);
        }

        async void INotificationService.NudgeUser(ChatUser user, ChatUser targetUser)
        {
            // Send a nudge message to the sender and the sendee
            await Clients.User(targetUser.Id).SendAsync("nudge", user.Name, targetUser.Name, null);

            await Clients.User(user.Id).SendAsync("nudge", user.Name, targetUser.Name, null);
        }

        async void INotificationService.NudgeRoom(ChatRoom room, ChatUser user)
        {
            await Clients.Group(room.Name).SendAsync("nudge", user.Name, null, room.Name);
        }

        async void INotificationService.LeaveRoom(ChatUser user, ChatRoom room)
        {
            await LeaveRoom(user, room);
        }

        async void INotificationService.OnUserNameChanged(ChatUser user, string oldUserName, string newUserName)
        {
            // Create the view model
            var userViewModel = new UserViewModel(user);


            // Tell the user's connected clients that the name changed
            await Clients.User(user.Id).SendAsync("UserNameChanged", userViewModel);

            // Notify all users in the rooms
            foreach (var room in user.Rooms)
            {
                await Clients.Group(room.Name).SendAsync("ChangeUserName", oldUserName, userViewModel, room.Name);
            }
        }

        void INotificationService.ChangeAfk(ChatUser user)
        {
            // Create the view model
            var userViewModel = new UserViewModel(user);

            // Tell all users in rooms to change the note
            foreach (var room in user.Rooms)
            {
                Clients.Group(room.Name).changeAfk(userViewModel, room.Name);
            }
        }

        void INotificationService.ChangeNote(ChatUser user)
        {
            // Create the view model
            var userViewModel = new UserViewModel(user);

            // Tell all users in rooms to change the note
            foreach (var room in user.Rooms)
            {
                Clients.Group(room.Name).changeNote(userViewModel, room.Name);
            }
        }

        void INotificationService.ChangeFlag(ChatUser user)
        {
            bool isFlagCleared = String.IsNullOrWhiteSpace(user.Flag);

            // Create the view model
            var userViewModel = new UserViewModel(user);

            // Update the calling client
            Clients.User(user.Id).flagChanged(isFlagCleared, userViewModel.Country);

            // Tell all users in rooms to change the flag
            foreach (var room in user.Rooms)
            {
                Clients.Group(room.Name).changeFlag(userViewModel, room.Name);
            }
        }

        void INotificationService.ChangeTopic(ChatUser user, ChatRoom room)
        {
            Clients.Group(room.Name).SendAsync("topicChanged", room.Name, room.Topic ?? String.Empty, user.Name).Wait();

            // trigger a lobby update
            OnRoomChangedAsync(room).Wait();
        }

        void INotificationService.ChangeWelcome(ChatUser user, ChatRoom room)
        {
            bool isWelcomeCleared = String.IsNullOrWhiteSpace(room.Welcome);
            var parsedWelcome = room.Welcome ?? String.Empty;
            Clients.User(user.Id).welcomeChanged(isWelcomeCleared, parsedWelcome);
        }

        void INotificationService.GenerateMeme(ChatUser user, ChatRoom room, string message)
        {
            Send(message, room.Name);
        }

        void INotificationService.AddAdmin(ChatUser targetUser)
        {
            // Tell this client it's an owner
            Clients.User(targetUser.Id).makeAdmin();

            var userViewModel = new UserViewModel(targetUser);

            // Tell all users in rooms to change the admin status
            foreach (var room in targetUser.Rooms)
            {
                Clients.Group(room.Name).addAdmin(userViewModel, room.Name);
            }

            // Tell the calling client the granting of admin status was successful
            Clients.Caller.adminMade(targetUser.Name);
        }

        void INotificationService.RemoveAdmin(ChatUser targetUser)
        {
            // Tell this client it's no longer an owner
            Clients.User(targetUser.Id).demoteAdmin();

            var userViewModel = new UserViewModel(targetUser);

            // Tell all users in rooms to change the admin status
            foreach (var room in targetUser.Rooms)
            {
                Clients.Group(room.Name).removeAdmin(userViewModel, room.Name);
            }

            // Tell the calling client the removal of admin status was successful
            Clients.Caller.adminRemoved(targetUser.Name);
        }

        void INotificationService.BroadcastMessage(ChatUser user, string messageText)
        {
            // Tell all users in all rooms about this message
            foreach (var room in _repository.Rooms)
            {
                Clients.Group(room.Name).broadcastMessage(messageText, room.Name);
            }
        }

        void INotificationService.ForceUpdate()
        {
            Clients.All.forceUpdate();
        }

        private async Task OnRoomChangedAsync(ChatRoom room)
        {
            var roomViewModel = new RoomViewModel
            {
                Name = room.Name,
                Private = room.Private,
                Closed = room.Closed,
                Topic = room.Topic ?? String.Empty,
                Count = _repository.GetOnlineUsers(room).Count()
            };

            // notify all clients who can see the room
            if (!room.Private)
            {
                await Clients.All.SendAsync("updateRoom", roomViewModel);
            }
            else
            {
                await Clients.Clients(_repository.GetAllowedClientIds(room)).SendAsync("updateRoom", roomViewModel);
            }
        }

        private void OnRoomChanged(ChatRoom room)
        {
            OnRoomChangedAsync(room).Wait();
        }

        private ClientState GetClientState()
        {
            // New client state
            var jabbrState = GetCookieValue("jabbr.state");

            ClientState clientState = null;

            if (String.IsNullOrEmpty(jabbrState))
            {
                clientState = new ClientState();
            }
            else
            {
                clientState = JsonConvert.DeserializeObject<ClientState>(jabbrState);
            }

            return clientState;
        }

        private string GetCookieValue(string key)
        {
            Cookie cookie;
            Context.RequestCookies.TryGetValue(key, out cookie);
            string value = cookie != null ? cookie.Value : null;
            return value != null ? Uri.UnescapeDataString(value) : null;
        }

        void INotificationService.BanUser(ChatUser targetUser, ChatUser callingUser, string reason)
        {
            var rooms = targetUser.Rooms.Select(x => x.Name).ToArray();
            var targetUserViewModel = new UserViewModel(targetUser);
            var callingUserViewModel = new UserViewModel(callingUser);

            if (String.IsNullOrWhiteSpace(reason))
            {
                reason = null;
            }
            
            // We send down room so that other clients can display that the user has been banned
            foreach (var room in rooms)
            {
                Clients.Group(room).ban(targetUserViewModel, room, callingUserViewModel, reason);   
            }

            foreach (var client in targetUser.ConnectedClients)
            {
                foreach (var room in rooms)
                {
                    // Remove the user from this the room group so he doesn't get the general ban message
                    Groups.Remove(client.Id, room);
                }
            }
        }

        void INotificationService.UnbanUser(ChatUser targetUser)
        {
            Clients.Caller.unbanUser(new
            {
                Name = targetUser.Name
            });
        }

        void INotificationService.CheckBanned()
        {
            // Get all users that are banned
            var users = _repository.Users.Where(u => u.IsBanned)
                                         .Select(u => u.Name)
                                         .OrderBy(u => u);

            Clients.Caller.listUsers(users);
        }

        void INotificationService.CheckBanned(ChatUser user)
        {
            Clients.Caller.checkBanned(new
            {
                Name = user.Name,
                IsBanned = user.IsBanned
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _repository.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}