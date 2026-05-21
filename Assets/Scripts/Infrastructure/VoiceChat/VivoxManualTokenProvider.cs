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
            string token;

            if (action == "login")
            {
                token = VivoxTokenGenerator.CreateLoginToken(_playerId);
            }
            else if (action == "join")
            {
                // Extract just the channel name from the channelUri
                // Format: sip:confctl-g-issuer.channelName@domain
                string channelName = ParseChannelName(channelUri);
                token = VivoxTokenGenerator.CreateJoinToken(_playerId, channelName);
            }
            else
            {
                // For other actions (mute, kick, etc.) fall back to a basic token
                token = VivoxTokenGenerator.CreateLoginToken(_playerId);
            }

            return Task.FromResult(token);
        }

        private string ParseChannelName(string channelUri)
        {
            if (string.IsNullOrEmpty(channelUri)) return "MainLobby";

            // sip:confctl-g-issuer.channelName@domain  =>  extract channelName
            try
            {
                // Find the dot after "confctl-g-issuer"
                int prefixEnd = channelUri.IndexOf(".", StringComparison.Ordinal);
                int atSign = channelUri.IndexOf("@", StringComparison.Ordinal);
                if (prefixEnd >= 0 && atSign > prefixEnd)
                {
                    return channelUri.Substring(prefixEnd + 1, atSign - prefixEnd - 1);
                }
            }
            catch
            {
                // ignored
            }
            return "MainLobby";
        }
    }
}
