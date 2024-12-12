using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using JabbR.Models;
using JabbR.Services;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

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
            var authResult = new AuthenticationResult
            {
                Success = true
            };

            var principal = ticket.Principal;
            var loggedInUser = GetLoggedInUser(principal);

            if (principal.Identity.IsAuthenticated)
            {
                EnsurePersistentCookie(ticket);
                return Task.FromResult(Guid.NewGuid().ToString());
            }

            var user = _repository.GetUser(principal);
            authResult.ProviderName = principal.Identity.AuthenticationType;

            if (user != null)
            {
                if (loggedInUser != null && user != loggedInUser)
                {
                    authResult.Message = String.Format(LanguageResources.Account_AccountAlreadyLinked, authResult.ProviderName);
                    authResult.Success = false;
                    AddClaim(ticket, new Claim(JabbRClaimTypes.Identifier, loggedInUser.Id));
                }
                else
                {
                    AddClaim(ticket, user);
                }
            }
            else if (HasAllClaims(principal))
            {
                ChatUser targetUser;

                if (loggedInUser == null)
                {
                    user = _membershipService.AddUser(principal);
                    targetUser = user;
                }
                else
                {
                    _membershipService.LinkIdentity(loggedInUser, principal);
                    _repository.CommitChanges();
                    authResult.Message = String.Format(LanguageResources.Account_AccountLinkedSuccess, authResult.ProviderName);
                    targetUser = loggedInUser;
                }

                AddClaim(ticket, targetUser);
            }
            else if (!HasPartialIdentity(principal))
            {
                AddClaim(ticket, new Claim(JabbRClaimTypes.PartialIdentity, "true"));
            }

            var key = Guid.NewGuid().ToString();
            // In a real implementation, you would store the ticket here
            return Task.FromResult(key);
        }

        public Task RenewAsync(string key, AuthenticationTicket ticket)
        {
            // Implement ticket renewal logic if needed
            return Task.CompletedTask;
        }

        public Task<AuthenticationTicket> RetrieveAsync(string key)
        {
            // Implement ticket retrieval logic
            // For now, we'll return null as we haven't implemented storage
            return Task.FromResult<AuthenticationTicket>(null);
        }

        public Task RemoveAsync(string key)
        {
            // Implement ticket removal logic if needed
            return Task.CompletedTask;
        }

        private void AddClaim(AuthenticationTicket ticket, ChatUser user)
        {
            if (user.IsBanned)
            {
                return;
            }

            AddClaim(ticket, new Claim(JabbRClaimTypes.Identifier, user.Id));

            if (user.IsAdmin)
            {
                AddClaim(ticket, new Claim(JabbRClaimTypes.Admin, "true"));
            }

            EnsurePersistentCookie(ticket);
        }

        private void AddClaim(AuthenticationTicket ticket, Claim claim)
        {
            var identity = ticket.Principal.Identity as ClaimsIdentity;
            identity?.AddClaim(claim);
        }

        private void EnsurePersistentCookie(AuthenticationTicket ticket)
        {
            ticket.Properties.IsPersistent = true;
        }

        private ChatUser GetLoggedInUser(ClaimsPrincipal principal)
        {
            return _repository.GetLoggedInUser(principal);
        }

        private bool HasAllClaims(ClaimsPrincipal principal)
        {
            // Implement this method based on your requirements
            return true;
        }

        private bool HasPartialIdentity(ClaimsPrincipal principal)
        {
            // Implement this method based on your requirements
            return false;
        }
    }
}