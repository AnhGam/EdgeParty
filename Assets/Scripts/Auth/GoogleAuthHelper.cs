using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace EdgeParty.Auth
{
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
        }

        public static void StartLogin(string clientId, string clientSecret, int port, Action<string> onSuccess, Action<string> onFailure)
        {
            if (_httpListener != null && _httpListener.IsListening)
            {
                _httpListener.Stop();
                _httpListener = null;
            }

            try
            {
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add($"http://localhost:{port}/");
                _httpListener.Start();
                
                string state = Guid.NewGuid().ToString("N");
                string redirectUri = $"http://localhost:{port}";
                
                // Construct Google OAuth URL
                string authUrl = $"https://accounts.google.com/o/oauth2/v2/auth" +
                                 $"?client_id={clientId}" +
                                 $"&redirect_uri={redirectUri}" +
                                 $"&response_type=code" +
                                 $"&scope=openid%20email%20profile" +
                                 $"&state={state}";

                // Open the user's default browser
                Application.OpenURL(authUrl);
                Debug.Log($"Google Login started. Opened browser Auth URL: {authUrl}");

                // Wait for the redirect response asynchronously
                ListenForRedirectAsync(clientId, clientSecret, redirectUri, state, onSuccess, onFailure);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to start Google OAuth Listener: {ex.Message}");
                onFailure?.Invoke($"Failed to start login listener: {ex.Message}");
            }
        }

        private static async void ListenForRedirectAsync(string clientId, string clientSecret, string redirectUri, string state, Action<string> onSuccess, Action<string> onFailure)
        {
            try
            {
                // Wait for HTTP request
                HttpListenerContext context = await _httpListener.GetContextAsync();
                HttpListenerRequest request = context.Request;
                HttpListenerResponse response = context.Response;

                string code = request.QueryString.Get("code");
                string incomingState = request.QueryString.Get("state");

                if (incomingState != state)
                {
                    SendHtmlResponse(response, "Authentication failed: State mismatch.", false);
                    onFailure?.Invoke("Authentication failed: State mismatch.");
                    StopListener();
                    return;
                }

                if (string.IsNullOrEmpty(code))
                {
                    SendHtmlResponse(response, "Authentication failed: No code returned.", false);
                    onFailure?.Invoke("Authentication failed: No code returned.");
                    StopListener();
                    return;
                }

                // Send success message to browser
                SendHtmlResponse(response, "Login successful! You can close this tab and return to the game.", true);
                StopListener();

                // Exchange Code for ID Token
                await ExchangeCodeForTokenAsync(clientId, clientSecret, redirectUri, code, onSuccess, onFailure);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error listening for redirect: {ex.Message}");
                onFailure?.Invoke($"Redirect listener error: {ex.Message}");
                StopListener();
            }
        }

        private static void SendHtmlResponse(HttpListenerResponse response, string message, bool isSuccess)
        {
            string color = isSuccess ? "#4CAF50" : "#F44336";
            string title = isSuccess ? "Success" : "Failed";

            string html = $@"
<!DOCTYPE html>
<html>
<head>
    <title>EdgeParty Login</title>
    <style>
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background-color: #0f0e17;
            color: #fffffe;
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
            margin: 0;
        }}
        .card {{
            background: #1f1e26;
            padding: 30px;
            border-radius: 12px;
            box-shadow: 0 8px 16px rgba(0,0,0,0.3);
            text-align: center;
            max-width: 400px;
        }}
        h1 {{ color: {color}; margin-top: 0; }}
        p {{ color: #a7a9be; font-size: 16px; }}
        .btn {{
            background-color: {color};
            color: white;
            border: none;
            padding: 10px 20px;
            border-radius: 6px;
            font-weight: bold;
            cursor: pointer;
            margin-top: 15px;
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
            {
                output.Write(buffer, 0, buffer.Length);
            }
        }

        private static async Task ExchangeCodeForTokenAsync(string clientId, string clientSecret, string redirectUri, string code, Action<string> onSuccess, Action<string> onFailure)
        {
            WWWForm form = new WWWForm();
            form.AddField("client_id", clientId);
            form.AddField("client_secret", clientSecret);
            form.AddField("code", code);
            form.AddField("grant_type", "authorization_code");
            form.AddField("redirect_uri", redirectUri);

            using (UnityWebRequest request = UnityWebRequest.Post(TokenUrl, form))
            {
                var asyncOp = request.SendWebRequest();
                while (!asyncOp.isDone)
                {
                    await Task.Yield();
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"Token exchange failed: {request.error}\nResponse: {request.downloadHandler.text}");
                    onFailure?.Invoke($"Token exchange failed: {request.error}");
                    return;
                }

                string jsonResponse = request.downloadHandler.text;
                GoogleTokenResponse responseData = JsonUtility.FromJson<GoogleTokenResponse>(jsonResponse);

                if (responseData != null && !string.IsNullOrEmpty(responseData.id_token))
                {
                    onSuccess?.Invoke(responseData.id_token);
                }
                else
                {
                    onFailure?.Invoke("Token response did not contain id_token.");
                }
            }
        }

        private static void StopListener()
        {
            if (_httpListener != null)
            {
                try
                {
                    if (_httpListener.IsListening)
                    {
                        _httpListener.Stop();
                    }
                }
                catch { }
                _httpListener = null;
            }
        }
    }
}
