using System;
using System.Collections.Generic;
using System.Linq;
using JabbR.Services;

namespace JabbR.Infrastructure
{
    public class AuthenticationService : IAuthenticationService
    {
        // private readonly AuthenticationProviderFactory _factory;

        public AuthenticationService(/*AuthenticationProviderFactory factory,*/ ApplicationSettings appSettings)
        {
            // _factory = factory;

            // Authentication provider setup code removed
        }

        public IEnumerable<IAuthenticationProvider> GetProviders()
        {
            // Temporary implementation
            return Enumerable.Empty<IAuthenticationProvider>();
        }
    }

    // Temporary interface to prevent compilation errors
    public interface IAuthenticationProvider { }
}