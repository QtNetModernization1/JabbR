using System;
using System.Security.Claims;
using System.Threading.Tasks;
using JabbR.Models;
using JabbR.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace JabbR.Infrastructure
{
    public class JabbRFormsAuthenticationProvider : ITicketStore
    {
        private readonly IJabbrRepository _repository;
        private readonly IMembershipService _membershipService;
        private readonly ILogger<JabbRFormsAuthenticationProvider> _logger;

        public JabbRFormsAuthenticationProvider(IJabbrRepository repository, IMembershipService membershipService, ILogger<JabbRFormsAuthenticationProvider> logger)
        {
            _repository = repository;
            _membershipService = membershipService;
            _logger = logger;
        }

        public async Task<string> StoreAsync(AuthenticationTicket ticket)
        {
            var authResult = new AuthenticationResult
            {
                Success = true
            };

            var principal = ticket.Principal;
            var loggedInUser = await GetLoggedInUserAsync(principal);

            if (principal.Identity.IsAuthenticated)
            {
                await EnsurePersistentCookieAsync(ticket);
                return Guid.NewGuid().ToString();
            }

            var user = await _repository.GetUserAsync(principal);
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
                    await AddClaimAsync(ticket, user);
                }
            }
            else if (await HasAllClaimsAsync(principal))
            {
                ChatUser targetUser;

                if (loggedInUser == null)
                {
                    user = await _membershipService.AddUserAsync(principal);
                    targetUser = user;
                }
                else
                {
                    await _membershipService.LinkIdentityAsync(loggedInUser, principal);
                    await _repository.CommitChangesAsync();
                    authResult.Message = String.Format(LanguageResources.Account_AccountLinkedSuccess, authResult.ProviderName);
                    targetUser = loggedInUser;
                }

                await AddClaimAsync(ticket, targetUser);
            }
            else if (!await HasPartialIdentityAsync(principal))
            {
                AddClaim(ticket, new Claim(JabbRClaimTypes.PartialIdentity, "true"));
            }

            var key = Guid.NewGuid().ToString();
            // Store the ticket for later retrieval
            // You might want to use a distributed cache or database for this in a production scenario
            // For simplicity, we're just returning the key here
            return key;
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

        private async Task AddClaimAsync(AuthenticationTicket ticket, ChatUser user)
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

            await EnsurePersistentCookieAsync(ticket);
        }

        private void AddClaim(AuthenticationTicket ticket, Claim claim)
        {
            var identity = ticket.Principal.Identity as ClaimsIdentity;
            identity?.AddClaim(claim);
        }

        private Task EnsurePersistentCookieAsync(AuthenticationTicket ticket)
        {
            ticket.Properties.IsPersistent = true;
            return Task.CompletedTask;
        }

        private async Task<ChatUser> GetLoggedInUserAsync(ClaimsPrincipal principal)
        {
            return await _repository.GetLoggedInUserAsync(principal);
        }

        private Task<bool> HasAllClaimsAsync(ClaimsPrincipal principal)
        {
            // Implement this method based on your requirements
            return Task.FromResult(true);
        }

        private Task<bool> HasPartialIdentityAsync(ClaimsPrincipal principal)
        {
            // Implement this method based on your requirements
            return Task.FromResult(false);
        }
    }
}