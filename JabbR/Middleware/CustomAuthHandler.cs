using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using JabbR.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;

namespace JabbR.Middleware
{
    public class CustomAuthHandler
    {
        private readonly RequestDelegate _next;

        public CustomAuthHandler(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            var claimsPrincipal = context.User;

            if (claimsPrincipal != null &&
                !(claimsPrincipal is WindowsPrincipal) &&
                claimsPrincipal.Identity.IsAuthenticated &&
                claimsPrincipal.HasClaim(ClaimTypes.NameIdentifier))
            {
                var identity = new ClaimsIdentity(claimsPrincipal.Claims, Constants.JabbRAuthType);

                var providerName = claimsPrincipal.FindFirstValue(ClaimTypes.AuthenticationMethod);

                if (String.IsNullOrEmpty(providerName))
                {
                    // If there's no provider name just add custom as the name
                    identity.AddClaim(new Claim(ClaimTypes.AuthenticationMethod, "Custom"));
                }

                await context.SignInAsync(Constants.JabbRAuthType, new ClaimsPrincipal(identity));
            }

            await _next(context);
        }
    }
}