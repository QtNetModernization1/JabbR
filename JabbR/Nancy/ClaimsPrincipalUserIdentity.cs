using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace JabbR.Nancy
{
    public class ClaimsPrincipalUserIdentity : IUserIdentity
    {
        public ClaimsPrincipalUserIdentity(ClaimsPrincipal claimsPrincipal)
        {
            ClaimsPrincipal = claimsPrincipal;
        }

        public ClaimsPrincipal ClaimsPrincipal { get; private set; }

        public IEnumerable<string> Claims
        {
            get => ClaimsPrincipal.Claims.Select(c => c.Value);
        }

        public string UserName
        {
            get => ClaimsPrincipal.Identity?.Name;
        }

        public string AuthenticationType => ClaimsPrincipal.Identity?.AuthenticationType;

        public bool IsAuthenticated => ClaimsPrincipal.Identity?.IsAuthenticated ?? false;
    }
}