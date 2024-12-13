using System.Text.RegularExpressions;
using System.IO;
using JabbR.Services;
using Nancy;
using Nancy.ErrorHandling;
using Nancy.Responses;
using Nancy.ViewEngines;

namespace JabbR.Nancy
{
    public class ErrorPageHandler : IStatusCodeHandler
    {
        private readonly IJabbrRepository _repository;
        private readonly IRootPathProvider _rootPathProvider;
        private readonly IViewRenderer _viewRenderer;

        public ErrorPageHandler(IJabbrRepository repository, IRootPathProvider rootPathProvider, IViewRenderer viewRenderer)
        {
            _repository = repository;
            _rootPathProvider = rootPathProvider;
            _viewRenderer = viewRenderer;
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

            var viewLocation = Path.Combine(_rootPathProvider.GetRootPath(), "Views", "errorPage.cshtml");
            var response = _viewRenderer.RenderView(viewLocation, model, context) as Response;

            if (response != null)
            {
                response.StatusCode = statusCode;
                context.Response = response;
            }
            else
            {
                context.Response = new TextResponse(statusCode, "An error occurred. Status code: " + (int)statusCode);
            }
        }
    }
}