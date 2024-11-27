using System.Text.RegularExpressions;
using System.IO;

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

        public ErrorPageHandler(IViewFactory viewFactory, IJabbrRepository repository)
        {
            _viewFactory = viewFactory;
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

            var model = new
            {
                Error = statusCode,
                ErrorCode = (int)statusCode,
                SuggestRoomName = suggestRoomName
            };

            try
            {
                var viewLocationContext = new ViewLocationContext { Context = context };
                var response = _viewFactory.RenderView("errorPage", model, viewLocationContext);
                var memoryStream = new MemoryStream();
                response.Contents.Invoke(memoryStream);
                memoryStream.Position = 0;
                var reader = new StreamReader(memoryStream);
                var content = reader.ReadToEnd();

                context.Response = new TextResponse(statusCode, content);
            }
            catch
            {
                context.Response = new TextResponse(statusCode, "An error occurred");
            }
        }
    }
}