using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using JabbR.Services;
using Microsoft.AspNetCore.SignalR;
using Ninject;

namespace JabbR.ContentProviders.Core
{
    public class JabbRHub : Hub
    {
        public async Task AddMessageContent(string messageId, string content, string roomName)
        {
            await Clients.Group(roomName).SendAsync("addMessageContent", messageId, content, roomName);
        }
    }

    public class ContentProviderProcessor
    {
        private readonly IKernel _kernel;
        private readonly IHubContext<JabbRHub> _hubContext;

        public ContentProviderProcessor(IKernel kernel, IHubContext<JabbRHub> hubContext)
        {
            _kernel = kernel;
            _hubContext = hubContext;
        }

        public void ProcessUrls(IEnumerable<string> links,
                                string roomName,
                                string messageId)
        {

            var resourceProcessor = _kernel.Get<IResourceProcessor>();
            
            var contentTasks = links.Select(resourceProcessor.ExtractResource).ToArray();

            Task.Factory.ContinueWhenAll(contentTasks, tasks =>
            {
                foreach (var task in tasks)
                {
                    if (task.IsFaulted)
                    {
                        Trace.TraceError(task.Exception.GetBaseException().Message);
                        continue;
                    }

                    if (task.Result == null || String.IsNullOrEmpty(task.Result.Content))
                    {
                        continue;
                    }

                    // Update the message with the content

                    // REVIEW: Does it even make sense to get multiple results?
                    using (var repository = _kernel.Get<IJabbrRepository>())
                    {
                        var message = repository.GetMessageById(messageId);

                        // Should this be an append?
                        message.HtmlContent = task.Result.Content;

                        repository.CommitChanges();
                    }

                    // Notify the room
                    _hubContext.Clients.Group(roomName).SendAsync("addMessageContent", messageId, task.Result.Content, roomName);
                }
            });
        }
    }
}