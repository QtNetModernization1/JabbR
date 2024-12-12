using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using JabbR.Services;
using Microsoft.AspNetCore.DataProtection;


namespace JabbR.Infrastructure
{
    public class JabbRDataProtection : IDataProtectionProvider
    {
        private readonly ICryptoService _cryptoService;
        public JabbRDataProtection(ICryptoService cryptoService)
        {
            _cryptoService = cryptoService;
        }

        public IDataProtector CreateProtector(string purpose)
        {
            return new JabbRDataProtector(_cryptoService);
        }

        private class JabbRDataProtector : IDataProtector
        {
            private readonly ICryptoService _cryptoService;

            public JabbRDataProtector(ICryptoService cryptoService)
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
                // In this simple implementation, we ignore the purpose
                return this;
            }
        }
    }
}