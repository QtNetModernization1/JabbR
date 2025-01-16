using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using JabbR.Services;
using Microsoft.AspNetCore.DataProtection;


namespace JabbR.Infrastructure
{
    public class JabbRDataProtection : IDataProtector
    {
        private readonly ICryptoService _cryptoService;
        public JabbRDataProtection(ICryptoService cryptoService)
        {
            _cryptoService = cryptoService;
        }

        public byte[] Protect(byte[] plaintext)
        {
            return _cryptoService.Protect(plaintext);
        }

        public byte[] Unprotect(byte[] protectedData)
        {
            return _cryptoService.Unprotect(protectedData);
        }

        public IDataProtector CreateProtector(string purpose)
        {
            // This method is not part of IDataProtector, but we'll keep it
            // in case it's needed elsewhere in the codebase
            return this;
        }
    }
}