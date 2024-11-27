using System;
using System.Collections.Generic;
using System.Linq;
using JabbR.Infrastructure;
using JabbR.Models;
using JabbR.Services;
using JabbR.ViewModels;
using Nancy;
using Nancy.ModelBinding;
using Nancy.Responses;
using PagedList.Core;

namespace JabbR.Nancy
{
    public class NotificationsModule : NancyModule
    {
        public NotificationsModule(IJabbrRepository repository,
                                   IChatService chatService,
                                   IChatNotificationService notificationService)
            : base("/notifications")
        {
            Get("/", parameters =>
            {
                if (!Context.CurrentUser.IsAuthenticated())
                {
                    return Response.AsRedirect($"~/account/login?returnUrl={Request.Url.Path}");
                }

                var request = this.Bind<NotificationRequestModel>();

                ChatUser user = repository.GetUserById(Context.CurrentUser.Identity.Name);
                int unreadCount = repository.GetUnreadNotificationsCount(user);
                IPagedList<NotificationViewModel> notifications = GetNotifications(repository, user, all: request.All, page: request.Page, roomName: request.Room);

                var viewModel = new NotificationsViewModel
                {
                    ShowAll = request.All,
                    UnreadCount = unreadCount,
                    Notifications = notifications,
                };

                return View["index", viewModel];
            });

            Post("/markasread", parameters =>
            {
                if (!Context.CurrentUser.IsAuthenticated())
                {
                    return HttpStatusCode.Forbidden;
                }

                int notificationId = Request.Form["notificationId"];

                Notification notification = repository.GetNotificationById(notificationId);

                if (notification == null)
                {
                    return HttpStatusCode.NotFound;
                }

                ChatUser user = repository.GetUserById(Context.CurrentUser.Identity.Name);

                if (notification.UserKey != user.Key)
                {
                    return HttpStatusCode.Forbidden;
                }

                notification.Read = true;
                repository.CommitChanges();

                UpdateUnreadCountInChat(repository, notificationService, user);

                return Response.AsJson(new { success = true });
            });

            Post("/markallasread", parameters =>
            {
                if (!Context.CurrentUser.IsAuthenticated())
                {
                    return HttpStatusCode.Forbidden;
                }

                ChatUser user = repository.GetUserById(Context.CurrentUser.Identity.Name);
                IList<Notification> unReadNotifications = repository.GetNotificationsByUser(user).Unread().ToList();

                if (!unReadNotifications.Any())
                {
                    return HttpStatusCode.NotFound;
                }

                foreach (var notification in unReadNotifications)
                {
                    notification.Read = true;
                }

                repository.CommitChanges();

                UpdateUnreadCountInChat(repository, notificationService, user);

                return Response.AsJson(new { success = true });
            });
        }

        private static void UpdateUnreadCountInChat(IJabbrRepository repository, IChatNotificationService notificationService,
                                                    ChatUser user)
        {
            var unread = repository.GetUnreadNotificationsCount(user);
            notificationService.UpdateUnreadMentions(user, unread);
        }

        private static IPagedList<NotificationViewModel> GetNotifications(IJabbrRepository repository, ChatUser user, bool all = false,
                                                                          int page = 1, int take = 20, string roomName = null)
        {
            IQueryable<Notification> notificationsQuery = repository.GetNotificationsByUser(user);

            if (!all)
            {
                notificationsQuery = notificationsQuery.Unread();
            }

            if (!String.IsNullOrWhiteSpace(roomName))
            {
                var room = repository.VerifyRoom(roomName);

                if (room != null)
                {
                    notificationsQuery = notificationsQuery.ByRoom(roomName);
                }
            }

            return notificationsQuery.OrderByDescending(n => n.Message.When)
                                     .Select(n => new NotificationViewModel()
                                     {
                                         NotificationKey = n.Key,
                                         FromUserName = n.Message.User.Name,
                                         FromUserImage = n.Message.User.Hash,
                                         Message = n.Message.Content,
                                         HtmlEncoded = n.Message.HtmlEncoded,
                                         RoomName = n.Room.Name,
                                         Read = n.Read,
                                         When = n.Message.When
                                     })
                                     .ToPagedList(page, take);
        }

        private class NotificationRequestModel
        {
            public NotificationRequestModel()
            {
                All = false;
                Page = 1;
                Room = null;
            }

            public bool All { get; set; }
            public int Page { get; set; }
            public string Room { get; set; }
        }
    }
}