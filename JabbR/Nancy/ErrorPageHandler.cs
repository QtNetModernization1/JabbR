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

        public ErrorPageHandler(IViewFactory viewFactory, IJabbrRepository repository)
        {
            _repository = repository;
            _viewFactory = viewFactory;
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

            var response = new ViewResponse(
                _viewFactory,
                "errorPage",
                new
                {
                    Error = statusCode,
                    ErrorCode = (int)statusCode,
                    SuggestRoomName = suggestRoomName
                });

            response.StatusCode = statusCode;
            context.Response = response;
        }
    }
}