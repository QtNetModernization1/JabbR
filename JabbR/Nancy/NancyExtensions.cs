using System;
using System.Collections.Generic;
using System.Security.Claims;
using JabbR.Infrastructure;
using JabbR.Models;
using Nancy;
using Nancy.Helpers;
using Nancy.Validation;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;


namespace JabbR.Nancy
{
    public static class NancyExtensions
    {
        public static Response SignIn(this NancyModule module, IEnumerable<Claim> claims)
        {
            var httpContext = module.Context.GetHttpContext();

            var identity = new ClaimsIdentity(claims, Constants.JabbRAuthType);
            var principal = new ClaimsPrincipal(identity);
            httpContext.SignInAsync(Constants.JabbRAuthType, principal);

            return module.AsRedirectQueryStringOrDefault("~/");
        }

        public static Response SignIn(this NancyModule module, ChatUser user)
        {
            var claims = new List<Claim>();
            claims.Add(new Claim(JabbRClaimTypes.Identifier, user.Id));

            // Add the admin claim if the user is an Administrator
            if (user.IsAdmin)
            {
                claims.Add(new Claim(JabbRClaimTypes.Admin, "true"));
            }

            return module.SignIn(claims);
        }

        public static void SignOut(this NancyModule module)
        {
            var httpContext = module.Context.GetHttpContext();
            httpContext.SignOutAsync(Constants.JabbRAuthType);
        }

        public static void AddValidationError(this NancyModule module, string propertyName, string errorMessage)
        {
            var errors = new Dictionary<string, IList<ModelValidationError>>();
            errors[propertyName] = new List<ModelValidationError> { new ModelValidationError(propertyName, errorMessage) };
            module.ModelValidationResult = new ModelValidationResult(errors);
        }

        public static AuthenticationResult GetAuthenticationResult(this NancyContext context)
        {
            string value;
            if (!context.Request.Cookies.TryGetValue(Constants.AuthResultCookie, out value) && 
                String.IsNullOrEmpty(value))
            {
                return null;
            }

            return JsonConvert.DeserializeObject<AuthenticationResult>(HttpUtility.UrlDecode(value));
        }

        public static void AddAlertMessage(this Request request, string messageType, string alertMessage)
        {
            var container = request.Session.GetSessionValue<AlertMessageStore>(AlertMessageStore.AlertMessageKey);

            if (container == null)
            {
                container = new AlertMessageStore();
            }

            container.AddMessage(messageType, alertMessage);

            request.Session.SetSessionValue(AlertMessageStore.AlertMessageKey, container);
        }

        public static ClaimsPrincipal GetPrincipal(this NancyModule module)
        {
            if (module.Context.CurrentUser == null || !module.Context.CurrentUser.IsAuthenticated)
            {
                return null;
            }

            return module.Context.CurrentUser as ClaimsPrincipal;
        }

        public static bool IsAuthenticated(this NancyModule module)
        {
            return module.GetPrincipal().IsAuthenticated();
        }

        public static Response AsRedirectQueryStringOrDefault(this NancyModule module, string defaultUrl)
        {
            string returnUrl = module.Request.Query.returnUrl;
            if (String.IsNullOrWhiteSpace(returnUrl))
            {
                returnUrl = defaultUrl;
            }

            return module.Response.AsRedirect(returnUrl);
        }

        private static HttpContext GetHttpContext(this NancyContext context)
        {
            return context.Items["HttpContext"] as HttpContext;
        }
    }
}