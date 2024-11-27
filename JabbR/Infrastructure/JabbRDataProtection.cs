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
            return _crypttoService.Unprotect(protectedData);
        }

        public IDataProtector CreateProtector(string purpose)
        {
            throw new NotImplementedException();
        }

        public byte[] DangerousUnprotect(byte[] protectedData, bool ignoreRevocationErrors, out bool requiresMigration, out bool wasRevoked)
        {
            requiresMigration = false;
            wasRevoked = false;
            return Unprotect(protectedData);
        }
    }
}