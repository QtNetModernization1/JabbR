using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace JabbR.Infrastructure
{
    public static class Http
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private const string _userAgent = "Mozilla/5.0 (compatible; MSIE 9.0; Windows NT 6.1; WOW64; Trident/5.0; MAAU)";

        static Http()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(_userAgent);
        }

        public static async Task<dynamic> GetJsonAsync(string url)
        {
            var response = await GetAsync(url, request =>
            {
                request.Headers.Accept.ParseAdd("application/json");
            });

            var content = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject(content);
        }

        public static async Task<HttpResponseMessage> GetAsync(Uri uri, Action<HttpRequestMessage> init = null)
        {
using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
            {
                init?.Invoke(request);
                return await _httpClient.SendAsync(request).ConfigureAwait(continueOnCapturedContext: false);
            }
        }

        public static Task<HttpResponseMessage> GetAsync(string url, Action<HttpRequestMessage> init = null)
        {
            return GetAsync(new Uri(url), init);
        }

        public static async Task<TResult> GetJsonAsync<TResult>(string url)
        {
            var response = await GetAsync(url, request =>
            {
                request.Headers.Accept.ParseAdd("application/json");
            });

            var content = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<TResult>(content);
        }
    }
}