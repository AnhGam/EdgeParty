using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace EdgeParty.Auth
{
    /// <summary>
    /// Handles Google OAuth 2.0 login using PKCE (Proof Key for Code Exchange).
    /// This is the correct flow for native/desktop apps — no client_secret required.
    /// Google documentation: https://developers.google.com/identity/protocols/oauth2/native-app
    /// </summary>
    public static class GoogleAuthHelper
    {
        private static HttpListener _httpListener;
        private const string TokenUrl = "https://oauth2.googleapis.com/token";

        [Serializable]
        private class GoogleTokenResponse
        {
            public string id_token;
            public string access_token;
            public int expires_in;
            public string token_type;
            public string scope;
            public string error;
            public string error_description;
        }

        public static void StartLogin(string clientId, int port, Action<string> onSuccess, Action<string> onFailure)
        {
            if (_httpListener != null && _httpListener.IsListening)
            {
                _httpListener.Stop();
                _httpListener = null;
            }

            try
            {
                // ── PKCE: Generate code_verifier and code_challenge ──────────
                string codeVerifier = GenerateCodeVerifier();
                string codeChallenge = GenerateCodeChallenge(codeVerifier);
                string state = Guid.NewGuid().ToString("N");
                string redirectUri = $"http://localhost:{port}";

                // ── Start local HTTP listener ────────────────────────────────
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add($"http://localhost:{port}/");
                _httpListener.Start();

                // ── Build Google OAuth URL with PKCE params ──────────────────
                string authUrl = "https://accounts.google.com/o/oauth2/v2/auth" +
                                 $"?client_id={Uri.EscapeDataString(clientId)}" +
                                 $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                                 "&response_type=code" +
                                 "&scope=openid%20email%20profile" +
                                 $"&state={state}" +
                                 $"&code_challenge={codeChallenge}" +
                                 "&code_challenge_method=S256" +
                                 "&access_type=offline";

                Application.OpenURL(authUrl);
                Debug.Log("[GoogleAuth] Browser opened for Google PKCE login.");

                ListenForRedirectAsync(clientId, codeVerifier, redirectUri, state, onSuccess, onFailure);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GoogleAuth] Failed to start OAuth listener: {ex.Message}");
                onFailure?.Invoke($"Failed to start login: {ex.Message}");
            }
        }

        // ── Legacy overload: ignores secret, forwards to PKCE flow ──────────
        // Kept for backward compatibility with any old call sites.
        public static void StartLogin(string clientId, string _ignoredSecret, int port, Action<string> onSuccess, Action<string> onFailure)
        {
            StartLogin(clientId, port, onSuccess, onFailure);
        }

        // ────────────────────────────────────────────────────────────────────

        private static async void ListenForRedirectAsync(
            string clientId, string codeVerifier, string redirectUri,
            string expectedState, Action<string> onSuccess, Action<string> onFailure)
        {
            try
            {
                HttpListenerContext context = await _httpListener.GetContextAsync();
                HttpListenerRequest request = context.Request;
                HttpListenerResponse response = context.Response;

                string code = request.QueryString.Get("code");
                string incomingState = request.QueryString.Get("state");
                string error = request.QueryString.Get("error");

                if (!string.IsNullOrEmpty(error))
                {
                    SendHtmlResponse(response, $"Login cancelled or denied: {error}", false);
                    onFailure?.Invoke($"Google login denied: {error}");
                    StopListener();
                    return;
                }

                if (incomingState != expectedState)
                {
                    SendHtmlResponse(response, "Security error: state mismatch.", false);
                    onFailure?.Invoke("Authentication failed: state mismatch.");
                    StopListener();
                    return;
                }

                if (string.IsNullOrEmpty(code))
                {
                    SendHtmlResponse(response, "Authentication failed: no code received.", false);
                    onFailure?.Invoke("Authentication failed: no code returned.");
                    StopListener();
                    return;
                }

                SendHtmlResponse(response, "Login successful! You can close this tab and return to the game.", true);
                StopListener();

                // Exchange code → id_token using PKCE verifier (no secret needed)
                await ExchangeCodeForTokenAsync(clientId, codeVerifier, redirectUri, code, onSuccess, onFailure);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GoogleAuth] Redirect listener error: {ex.Message}");
                onFailure?.Invoke($"Redirect listener error: {ex.Message}");
                StopListener();
            }
        }

        private static async Task ExchangeCodeForTokenAsync(
            string clientId, string codeVerifier, string redirectUri,
            string code, Action<string> onSuccess, Action<string> onFailure)
        {
            // PKCE token exchange — no client_secret
            WWWForm form = new WWWForm();
            form.AddField("client_id", clientId);
            form.AddField("code", code);
            form.AddField("code_verifier", codeVerifier);
            form.AddField("grant_type", "authorization_code");
            form.AddField("redirect_uri", redirectUri);

            using (UnityWebRequest request = UnityWebRequest.Post(TokenUrl, form))
            {
                var asyncOp = request.SendWebRequest();
                while (!asyncOp.isDone)
                    await Task.Yield();

                string responseText = request.downloadHandler.text;

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[GoogleAuth] Token exchange failed: {request.error}\nResponse: {responseText}");
                    onFailure?.Invoke($"Token exchange failed: {request.error}");
                    return;
                }

                GoogleTokenResponse tokenData = JsonUtility.FromJson<GoogleTokenResponse>(responseText);

                if (tokenData != null && !string.IsNullOrEmpty(tokenData.id_token))
                {
                    Debug.Log("[GoogleAuth] Google id_token received successfully.");
                    onSuccess?.Invoke(tokenData.id_token);
                }
                else
                {
                    string errMsg = tokenData?.error_description ?? tokenData?.error ?? "Unknown error";
                    Debug.LogError($"[GoogleAuth] Token response missing id_token. Reason: {errMsg}\nFull response: {responseText}");
                    onFailure?.Invoke($"Google login failed: {errMsg}");
                }
            }
        }

        // ── PKCE Helpers ─────────────────────────────────────────────────────

        /// <summary>Generates a cryptographically random code_verifier (43-128 chars, RFC 7636).</summary>
        private static string GenerateCodeVerifier()
        {
            byte[] bytes = new byte[64];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return Base64UrlEncode(bytes);
        }

        /// <summary>Derives code_challenge = BASE64URL(SHA256(code_verifier)).</summary>
        private static string GenerateCodeChallenge(string codeVerifier)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
                return Base64UrlEncode(hash);
            }
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        // ── HTML response page ───────────────────────────────────────────────

        private static void SendHtmlResponse(HttpListenerResponse response, string message, bool isSuccess)
        {
            string color = isSuccess ? "#4CAF50" : "#F44336";
            string title = isSuccess ? "✓ Login Successful" : "✗ Login Failed";

            string html = $@"<!DOCTYPE html>
<html>
<head>
    <title>EdgeParty Login</title>
    <style>
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background: linear-gradient(135deg, #0f0e17 0%, #1a1a2e 100%);
            color: #fffffe;
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
            margin: 0;
        }}
        .card {{
            background: rgba(31,30,38,0.95);
            padding: 40px;
            border-radius: 16px;
            box-shadow: 0 12px 40px rgba(0,0,0,0.5);
            text-align: center;
            max-width: 420px;
            border: 1px solid rgba(255,255,255,0.08);
        }}
        h1 {{ color: {color}; margin-top: 0; font-size: 1.8em; }}
        p {{ color: #a7a9be; font-size: 15px; line-height: 1.5; }}
        .btn {{
            background-color: {color};
            color: white;
            border: none;
            padding: 12px 24px;
            border-radius: 8px;
            font-weight: bold;
            cursor: pointer;
            margin-top: 15px;
            font-size: 14px;
        }}
    </style>
</head>
<body>
    <div class='card'>
        <h1>{title}</h1>
        <p>{message}</p>
        <button class='btn' onclick='window.close()'>Close Tab</button>
    </div>
</body>
</html>";

            byte[] buffer = Encoding.UTF8.GetBytes(html);
            response.ContentLength64 = buffer.Length;
            response.ContentType = "text/html; charset=utf-8";
            using (Stream output = response.OutputStream)
                output.Write(buffer, 0, buffer.Length);
        }

        private static void StopListener()
        {
            if (_httpListener == null) return;
            try { if (_httpListener.IsListening) _httpListener.Stop(); }
            catch { }
            _httpListener = null;
        }
    }
}
