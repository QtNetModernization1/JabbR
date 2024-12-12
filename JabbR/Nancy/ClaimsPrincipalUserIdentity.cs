using System.Collections.Generic;
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
            get;
            set;
        }

        public string UserName
        {
            get;
            set;
        }
    }

    public interface IUserIdentity
    {
        string UserName { get; }
        IEnumerable<string> Claims { get; }
    }
}