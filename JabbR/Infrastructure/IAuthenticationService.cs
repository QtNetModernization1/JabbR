using System.Collections.Generic;

using SimpleAuthentication;

namespace JabbR.Infrastructure
{
    public interface IAuthenticationService
    {
        IEnumerable<IAuthenticationProvider> GetProviders();
    }
}