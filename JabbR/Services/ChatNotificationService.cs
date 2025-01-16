using System;
using System.Linq;
using JabbR.Models;
using JabbR.ViewModels;
using Microsoft.AspNetCore.SignalR;

namespace JabbR.Services
{
    public class ChatNotificationService : IChatNotificationService
    {
        private readonly IHubContext<Chat> _hubContext;

        public ChatNotificationService(IHubContext<Chat> hubContext)
        {
            _hubContext = hubContext;
        }

        public void OnUserNameChanged(ChatUser user, string oldUserName, string newUserName)
        {
            // Create the view model
            var userViewModel = new UserViewModel(user);

            // Tell the user's connected clients that the name changed
            foreach (var client in user.ConnectedClients)
            {
                _hubContext.Clients.Client(client.Id).SendAsync("userNameChanged", userViewModel);
            }

            // Notify all users in the rooms
            foreach (var room in user.Rooms)
            {
                _hubContext.Clients.Group(room.Name).SendAsync("changeUserName", oldUserName, userViewModel, room.Name);
            }
        }

        public void UpdateUnreadMentions(ChatUser mentionedUser, int unread)
        {
            foreach (var client in mentionedUser.ConnectedClients)
            {
                _hubContext.Clients.Client(client.Id).SendAsync("updateUnreadNotifications", unread);
            }
        }
    }
}