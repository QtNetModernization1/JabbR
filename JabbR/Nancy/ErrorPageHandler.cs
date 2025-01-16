using System;
using System.IO;
using System.Text.RegularExpressions;

using JabbR.Services;

using Nancy;
using Nancy.ErrorHandling;
using Nancy.ViewEngines;
using Nancy.Responses;

namespace JabbR.Nancy
{
    public class ErrorPageHandler : IStatusCodeHandler
    {
        private readonly IJabbrRepository _repository;
        private readonly IViewFactory _viewFactory;

        public ErrorPageHandler(IViewFactory factory, IJabbrRepository repository)
        {
            _viewFactory = factory;
            _repository = repository;
        }

        public bool HandlesStatusCode(HttpStatusCode statusCode, NancyContext context)
        {
            // only handle 40x and 50x
            return (int)statusCode >= 400;
        }

        public void Handle(HttpStatusCode statusCode, NancyContext context)
        {
            string suggestRoomName = null;
            if (statusCode == HttpStatusCode.NotFound)
            {
                var match = Regex.Match(context.Request.Url.Path, "^/(rooms/)?(?<roomName>[^/]+)$", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var potentialRoomName = match.Groups["roomName"].Value;
                    if (_repository.GetRoomByName(potentialRoomName) != null)
                    {
                        suggestRoomName = potentialRoomName;
                    }
                }
            }

            var viewLocationContext = new ViewLocationContext { Context = context };

            var response = new Response
            {
                Contents = stream =>
                {
                    var view = _viewFactory.RenderView(
                        "errorPage",
                        new
                        {
                            Error = statusCode,
                            ErrorCode = (int)statusCode,
                            SuggestRoomName = suggestRoomName
                        },
                        viewLocationContext);
using (var writer = new StreamWriter(stream))
                    {
                        writer.Write(view);
                        writer.Flush();
                    }
                },
                ContentType = "text/html",
                StatusCode = statusCode
            };

            context.Response = response;
        }
    }
}