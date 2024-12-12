using System;
using System.Collections.Generic;
using System.Security.Claims;
using JabbR.Infrastructure;
using JabbR.Models;
using Nancy;
using Nancy.Helpers;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using System.Linq;


namespace JabbR.Nancy
{
    public class ValidationResult
    {
        public List<ValidationError> Errors { get; } = new List<ValidationError>();
    }

    public class ValidationError
    {
        public string PropertyName { get; set; }
        public string ErrorMessage { get; set; }

        public ValidationError(string propertyName, string errorMessage)
        {
            PropertyName = propertyName;
            ErrorMessage = errorMessage;
        }
    }

    public static class NancyExtensions
    {
        private static string ValidationResultKey = "ValidationResult";

        public static Response SignIn(this NancyModule module, IEnumerable<Claim> claims, IHttpContextAccessor httpContextAccessor)
        {
            var identity = new ClaimsIdentity(claims, Constants.JabbRAuthType);
            var principal = new ClaimsPrincipal(identity);

            httpContextAccessor.HttpContext.SignInAsync(Constants.JabbRAuthType, principal);

            return module.AsRedirectQueryStringOrDefault("~/");
        }

        public static Response SignIn(this NancyModule module, ChatUser user, IHttpContextAccessor httpContextAccessor)
        {
            var claims = new List<Claim>();
            claims.Add(new Claim(JabbRClaimTypes.Identifier, user.Id));

            // Add the admin claim if the user is an Administrator
            if (user.IsAdmin)
            {
                claims.Add(new Claim(JabbRClaimTypes.Admin, "true"));
            }

            return module.SignIn(claims, httpContextAccessor);
        }

        public static void SignOut(this NancyModule module, IHttpContextAccessor httpContextAccessor)
        {
            httpContextAccessor.HttpContext.SignOutAsync(Constants.JabbRAuthType);
        }

        public static void AddValidationError(this NancyModule module, string propertyName, string errorMessage)
        {
            var validationResult = module.Context.Items.ContainsKey(ValidationResultKey)
                ? (ValidationResult)module.Context.Items[ValidationResultKey]
                : new ValidationResult();

            validationResult.Errors.Add(new ValidationError(propertyName, errorMessage));
            module.Context.Items[ValidationResultKey] = validationResult;
        }

        public static bool HasValidationErrors(this NancyModule module)
        {
            return module.Context.Items.ContainsKey(ValidationResultKey) &&
                   ((ValidationResult)module.Context.Items[ValidationResultKey]).Errors.Any();
        }

        public static IEnumerable<ValidationError> GetValidationErrors(this NancyModule module)
        {
            return module.Context.Items.ContainsKey(ValidationResultKey)
                ? ((ValidationResult)module.Context.Items[ValidationResultKey]).Errors
                : Enumerable.Empty<ValidationError>();
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
            var userIdentity = module.Context.CurrentUser as ClaimsPrincipalUserIdentity;

            if (userIdentity == null)
            {
                return null;
            }

            return userIdentity.ClaimsPrincipal;
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

        private static T Get<T>(IDictionary<string, object> env, string key)
        {
            object value;
            if (env.TryGetValue(key, out value))
            {
                return (T)value;
            }
            return default(T);
        }
    }
}