using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.RegularExpressions;

using JabbR.Services;
using Nancy;

namespace JabbR.Nancy
{
    public class JabbRAuthenticationCallbackProvider
    {
        private readonly IJabbrRepository _repository;

        public JabbRAuthenticationCallbackProvider(IJabbrRepository repository)
        {
            _repository = repository;
        }

        public dynamic Process(NancyModule nancyModule, dynamic model)
        {
            Response response;

            if (model.ReturnUrl != null)
            {
                response = nancyModule.Redirect("~" + model.ReturnUrl);
            }
            else
            {
                response = nancyModule.Redirect("~/");

                if (nancyModule.Context.CurrentUser != null)
                {
                    response = nancyModule.Redirect("~/account/#identityProviders");
                }
            }

            if (model.Exception != null)
            {
                nancyModule.ViewBag.ErrorMessage = model.Exception.Message;
            }
            else
            {
                var claims = new List<Claim>();
                claims.Add(new Claim(ClaimTypes.NameIdentifier, model.Id));
                claims.Add(new Claim(ClaimTypes.AuthenticationMethod, model.ProviderName));

                if (!String.IsNullOrEmpty(model.UserName))
                {
                    claims.Add(new Claim(ClaimTypes.Name, model.UserName));
                }

                if (!String.IsNullOrEmpty(model.Email))
                {
                    claims.Add(new Claim(ClaimTypes.Email, model.Email));
                }

                var identity = new ClaimsIdentity(claims, "JabbR");
                nancyModule.Context.CurrentUser = new ClaimsPrincipal(identity);
            }

            return response;
        }

        public dynamic OnRedirectToAuthenticationProviderError(NancyModule nancyModule, string errorMessage)
        {
            nancyModule.ViewBag.ErrorMessage = errorMessage;
            return nancyModule.Redirect("~/error");
        }
    }
}