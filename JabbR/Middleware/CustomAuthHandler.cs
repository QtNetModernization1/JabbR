using System;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using JabbR.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;

namespace JabbR.Middleware
{
    public class CustomAuthHandler : IMiddleware
    {
        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            var claimsPrincipal = context.User;

            if (claimsPrincipal != null &&
                !(claimsPrincipal is WindowsPrincipal) &&
                claimsPrincipal.Identity.IsAuthenticated &&
                !claimsPrincipal.Identity.IsAuthenticated &&
                claimsPrincipal.HasClaim(ClaimTypes.NameIdentifier))
            {
                var identity = new ClaimsIdentity(claimsPrincipal.Claims, Constants.JabbRAuthType);

                var providerName = claimsPrincipal.FindFirstValue(ClaimTypes.AuthenticationMethod);

                if (string.IsNullOrEmpty(providerName))
                {
                    // If there's no provider name just add custom as the name
                    identity.AddClaim(new Claim(ClaimTypes.AuthenticationMethod, "Custom"));
                }

                await context.SignInAsync(Constants.JabbRAuthType, new ClaimsPrincipal(identity));
            }

            await next(context);
        }
    }
}