using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace RimMind.Infrastructure.Services.Clients
{
    internal static class HttpTransport
    {
        private static readonly HttpClient _http = new HttpClient();

        public class HttpException : Exception
        {
            public int StatusCode { get; }
            public HttpException(string message, int statusCode) : base(message) { StatusCode = statusCode; }
        }

        public static async Task<(string body, long statusCode)> PostAsync(
            string url, string jsonBody, string? authHeader = null,
            string? headerName = null, string? headerValue = null,
            float connectTimeout = 60f)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            if (authHeader != null)
                request.Headers.TryAddWithoutValidation("Authorization", authHeader);
            if (headerName != null && headerValue != null)
                request.Headers.TryAddWithoutValidation(headerName, headerValue);

            using var response = await _http.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();
            long statusCode = (long)response.StatusCode;

            if (!response.IsSuccessStatusCode)
                throw new HttpException(body ?? response.ReasonPhrase ?? $"HTTP {(int)statusCode}", (int)statusCode);

            return (body, statusCode);
        }

        public static async Task<(string body, long statusCode)> GetAsync(
            string url, string? authHeader = null,
            string? headerName = null, string? headerValue = null,
            float connectTimeout = 60f)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (authHeader != null)
                request.Headers.TryAddWithoutValidation("Authorization", authHeader);
            if (headerName != null && headerValue != null)
                request.Headers.TryAddWithoutValidation(headerName, headerValue);

            using var response = await _http.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();
            long statusCode = (long)response.StatusCode;

            if (!response.IsSuccessStatusCode)
                throw new HttpException(body ?? response.ReasonPhrase ?? $"HTTP {(int)statusCode}", (int)statusCode);

            return (body, statusCode);
        }
    }
}
