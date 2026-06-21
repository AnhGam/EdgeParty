using System;
using System.Threading.Tasks;
using Unity.Services.Vivox;

namespace EdgeParty.Infrastructure.VoiceChat
{
    /// <summary>
    /// Custom Vivox token provider that generates tokens client-side using the Token Key.
    /// WARNING: For production, move token generation to a secure backend.
    /// </summary>
    public class VivoxManualTokenProvider : IVivoxTokenProvider
    {
        private readonly string _playerId;

        public VivoxManualTokenProvider(string playerId)
        {
            _playerId = playerId;
        }

        // Exact signature from IVivoxTokenProvider in com.unity.services.vivox@16.x
        public Task<string> GetTokenAsync(
            string issuer = null,
            TimeSpan? expiration = null,
            string targetUserUri = null,
            string action = null,
            string channelUri = null,
            string fromUserUri = null,
            string realm = null)
        {
            UnityEngine.Debug.Log($"[VivoxTokenProvider] GetTokenAsync: action='{action}', fromUserUri='{fromUserUri}', channelUri='{channelUri}', targetUserUri='{targetUserUri}'");

            string token;

            if (action == "login")
            {
                token = VivoxTokenGenerator.CreateLoginToken(fromUserUri);
            }
            else if (action == "join")
            {
                token = VivoxTokenGenerator.CreateJoinToken(fromUserUri, channelUri);
            }
            else
            {
                token = VivoxTokenGenerator.CreateLoginToken(fromUserUri);
            }

            return Task.FromResult(token);
        }

    }
}
