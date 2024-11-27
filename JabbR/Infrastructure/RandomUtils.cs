using System;
using System.Security.Cryptography;

namespace JabbR.Infrastructure
{
    internal static class RandomUtils
    {
        public static string NextInviteCode()
        {
            // Generate a new invite code
            string code;
            byte[] data = new byte[4];
            RandomNumberGenerator.Fill(data);
            int value = BitConverter.ToInt32(data, 0);
            value = Math.Abs(value) % 1000000;
            code = value.ToString("000000");
            return code;
        }
    }
}