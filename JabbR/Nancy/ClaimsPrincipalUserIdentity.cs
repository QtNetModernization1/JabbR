using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Nancy.Security;

namespace JabbR.Nancy
{
    public class ClaimsPrincipalUserIdentity : IUserIdentity
    {
        public ClaimsPrincipalUserIdentity(ClaimsPrincipal claimsPrincipal)
        {
            ClaimsPrincipal = claimsPrincipal;
            UserName = claimsPrincipal.Identity?.Name ?? string.Empty;
            Claims = claimsPrincipal.Claims.Select(c => c.Value).ToList();
        }

        public ClaimsPrincipal ClaimsPrincipal { get; }

        public IEnumerable<string> Claims { get; }

        public string UserName { get; }
    }
}