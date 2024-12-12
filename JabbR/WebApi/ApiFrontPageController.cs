using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.Mvc;
using JabbR.Infrastructure;
using JabbR.Services;
using JabbR.WebApi.Model;

namespace JabbR.WebApi
{
    [ApiController]
    [Route("api/[controller]")]
    public class ApiFrontPageController : ControllerBase
    {
        private ApplicationSettings _appSettings;
        private readonly IUrlHelper _urlHelper;

        public ApiFrontPageController(ApplicationSettings appSettings, IUrlHelper urlHelper)
        {
            _appSettings = appSettings;
            _urlHelper = urlHelper;
        }

        /// <summary>
        /// Returns an absolute URL (including host and protocol) that corresponds to the relative path passed as an argument.
        /// </summary>
        /// <param name="sitePath">Path within the aplication, may contain ~ to denote the application root</param>
/// <returns>A URL that corresponds to requested path using host and protocol of the request</returns>
        public string ToAbsoluteUrl(string sitePath)
        {
            var request = HttpContext.Request;
            var host = request.Host.ToUriComponent();
            var scheme = request.Scheme;

            return $"{scheme}://{host}{_urlHelper.Content(sitePath)}";
        }

        public IActionResult GetFrontPage()
        {
            var responseData = new ApiFrontpageModel
            {
                MessagesUri = ToAbsoluteUrl(GetMessagesUrl())
            };

            return Ok(responseData);
        }

        private string GetMessagesUrl() {
            //hardcoded for now, needs a better place - i.e. some sort of constants.cs. 
            //Alternatively there might be a better way to do that in WebAPI
            return "/api/v1/messages/{room}/{format}";
        }
    }
}