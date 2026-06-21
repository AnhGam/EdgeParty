using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace EdgeParty.Infrastructure.VoiceChat
{
    /// <summary>
    /// Utility class to generate Vivox access tokens on the client side.
    /// WARNING: This should only be used for development. Token generation should move to a backend for production.
    /// </summary>
    public static class VivoxTokenGenerator
    {
        private static readonly string Header = "{}"; // Encodes to "e30" as required by Vivox
        private static int _vxi = 1;

        private static int GetNextVxi()
        {
            return System.Threading.Interlocked.Increment(ref _vxi);
        }

        public static string CreateLoginToken(string fromUserUri)
        {
            var claims = new Dictionary<string, object>
            {
                { "iss", VivoxConfig.TokenIssuer },
                { "exp", GetExpiry() },
                { "vxa", "login" },
                { "f", fromUserUri },
                { "vxi", GetNextVxi() }
            };

            return GenerateToken(claims);
        }

        public static string CreateJoinToken(string fromUserUri, string channelUri)
        {
            var claims = new Dictionary<string, object>
            {
                { "iss", VivoxConfig.TokenIssuer },
                { "exp", GetExpiry() },
                { "vxa", "join" },
                { "f", fromUserUri },
                { "t", channelUri },
                { "vxi", GetNextVxi() }
            };

            return GenerateToken(claims);
        }

        private static long GetExpiry()
        {
            // Token valid for 90 seconds (standard for Vivox)
            return (long)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds + 90;
        }

        private static string GenerateToken(Dictionary<string, object> claims)
        {
            string payload = "{";
            int count = 0;
            foreach (var kvp in claims)
            {
                payload += $"\"{kvp.Key}\":";
                if (kvp.Value is string s) payload += $"\"{s}\"";
                else payload += kvp.Value.ToString();
                
                if (++count < claims.Count) payload += ",";
            }
            payload += "}";

            string encodedHeader = Base64UrlEncode(Encoding.UTF8.GetBytes(Header));
            string encodedPayload = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));

            string signatureInput = $"{encodedHeader}.{encodedPayload}";
            byte[] keyBytes = Encoding.UTF8.GetBytes(VivoxConfig.TokenKey);
            
            using (var hmac = new HMACSHA256(keyBytes))
            {
                byte[] signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(signatureInput));
                string encodedSignature = Base64UrlEncode(signatureBytes);
                return $"{signatureInput}.{encodedSignature}";
            }
        }

        private static string Base64UrlEncode(byte[] input)
        {
            string base64 = Convert.ToBase64String(input);
            return base64.Replace('+', '-').Replace('/', '_').Replace("=", "");
        }
    }
}
