using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace JabbR.Infrastructure
{
    public class NamedClaimRequirement : IAuthorizationRequirement
    {
        public string ClaimType { get; }

        public NamedClaimRequirement(string claimType)
        {
            ClaimType = claimType;
        }
    }

    public class AuthorizeClaim : AuthorizationHandler<NamedClaimRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, NamedClaimRequirement requirement)
        {
            if (context.User.HasClaim(c => c.Type == requirement.ClaimType))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}