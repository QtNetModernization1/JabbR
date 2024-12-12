using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace JabbR.Infrastructure
{
    public class AuthorizeClaim : AuthorizeAttribute, IAuthorizationFilter
    {
        private readonly string _claimType;
        public AuthorizeClaim(string claimType)
        {
            _claimType = claimType;
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var user = context.HttpContext.User;

            if (user == null || !user.HasClaim(claim => claim.Type == _claimType))
            {
                context.Result = new ForbidResult();
            }
        }
    }
}