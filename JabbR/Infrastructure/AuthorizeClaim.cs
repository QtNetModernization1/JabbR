using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace JabbR.Infrastructure
{
    public class AuthorizeClaim : AuthorizeAttribute
    {
        private readonly string _claimType;
        public AuthorizeClaim(string claimType)
        {
            _claimType = claimType;
        }

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, IAuthorizationRequirement requirement)
        {
            if (context.User.HasClaim(claim => claim.Type == _claimType))
            {
                context.Succeed(requirement);
            }
            else
            {
                context.Fail();
            }

            return Task.CompletedTask;
        }
    }
}