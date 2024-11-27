using System.Collections.Generic;

using SimpleAuthentication.Core;
using SimpleAuthentication.Core.Providers;

namespace JabbR.Infrastructure
{
    public interface IAuthenticationService
    {
        IEnumerable<IAuthenticationProvider> GetProviders();
    }
}