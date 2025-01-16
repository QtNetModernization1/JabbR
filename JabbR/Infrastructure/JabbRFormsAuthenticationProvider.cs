using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using JabbR.Models;
using JabbR.Services;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace JabbR.Infrastructure
{
    public class JabbRFormsAuthenticationProvider : ITicketStore
    {
        private readonly IJabbrRepository _repository;
        private readonly IMembershipService _membershipService;

        public JabbRFormsAuthenticationProvider(IJabbrRepository repository, IMembershipService membershipService)
        {
            _repository = repository;
            _membershipService = membershipService;
        }

        public Task<string> StoreAsync(AuthenticationTicket ticket)
        {
            // Implement ticket storage logic
            throw new NotImplementedException();
        }

        public Task RenewAsync(string key, AuthenticationTicket ticket)
        {
            // Implement ticket renewal logic
            throw new NotImplementedException();
        }

        public Task<AuthenticationTicket> RetrieveAsync(string key)
        {
            // Implement ticket retrieval logic
            throw new NotImplementedException();
        }

        public Task RemoveAsync(string key)
        {
            // Implement ticket removal logic
            throw new NotImplementedException();
        }

        private void ResponseSignIn(ClaimsPrincipal principal, AuthenticationProperties properties)
        {
            var authResult = new AuthenticationResult
            {
                Success = true
            };

            ChatUser loggedInUser = GetLoggedInUser(principal);

            // Do nothing if it's authenticated
            if (principal.Identity.IsAuthenticated)
            {
                EnsurePersistentCookie(properties);
                return;
            }

            ChatUser user = _repository.GetUser(principal);
            authResult.ProviderName = principal.Identity.AuthenticationType;

            // The user exists so add the claim
            if (user != null)
            {
                if (loggedInUser != null && user != loggedInUser)
                {
                    // Set an error message
                    authResult.Message = String.Format(LanguageResources.Account_AccountAlreadyLinked, authResult.ProviderName);
                    authResult.Success = false;

                    // Keep the old user logged in
                    context.Identity.AddClaim(new Claim(JabbRClaimTypes.Identifier, loggedInUser.Id));
                }
                else
                {
                    // Login this user
                    AddClaim(context, user);
                }

            }
            else if (principal.HasAllClaims())
            {
                ChatUser targetUser = null;

                // The user doesn't exist but the claims to create the user do exist
                if (loggedInUser == null)
                {
                    // New user so add them
                    user = _membershipService.AddUser(principal);

                    targetUser = user;
                }
                else
                {
                    // If the user is logged in then link
                    _membershipService.LinkIdentity(loggedInUser, principal);

                    _repository.CommitChanges();

                    authResult.Message = String.Format(LanguageResources.Account_AccountLinkedSuccess, authResult.ProviderName);

                    targetUser = loggedInUser;
                }

                AddClaim(context, targetUser);
            }
            else if(!principal.HasPartialIdentity())
            {
                // A partial identity means the user needs to add more claims to login
                context.Identity.AddClaim(new Claim(JabbRClaimTypes.PartialIdentity, "true"));
            }

            var cookieOptions = new CookieOptions
            {
                HttpOnly = true
            };

            context.Response.Cookies.Append(Constants.AuthResultCookie,
                                       JsonConvert.SerializeObject(authResult),
                                       cookieOptions);
        }

        private static void AddClaim(ClaimsIdentity identity, ChatUser user)
        {
            // Do nothing if the user is banned
            if (user.IsBanned)
            {
                return;
            }

            // Add the jabbr id claim
            identity.AddClaim(new Claim(JabbRClaimTypes.Identifier, user.Id));

            // Add the admin claim if the user is an Administrator
            if (user.IsAdmin)
            {
                identity.AddClaim(new Claim(JabbRClaimTypes.Admin, "true"));
            }
        }

        private static void EnsurePersistentCookie(AuthenticationProperties properties)
        {
            if (properties == null)
            {
                properties = new AuthenticationProperties();
            }

            properties.IsPersistent = true;
        }

        private ChatUser GetLoggedInUser(ClaimsPrincipal principal)
        {
            return principal != null ? _repository.GetLoggedInUser(principal) : null;
        }
    }
}