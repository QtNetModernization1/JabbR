using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using JabbR.ContentProviders.Core;
using JabbR.Infrastructure;

namespace JabbR.ContentProviders
{
    public class UStreamContentProvider : CollapsibleContentProvider
    {
        private static readonly Regex _extractEmbedCodeRegex = new Regex(@"<textarea\s.*class=""embedCode"".*>(.*)</textarea>");

        protected override Task<ContentProviderResult> GetCollapsibleContent(ContentProviderHttpRequest request)
        {
            return ExtractIFrameCode(request).Then(result =>
            {
                var iframeHtml = result;
                return new ContentProviderResult()
                {
                    Content = iframeHtml.Replace("http://", "https://"),
                    Title = request.RequestUri.AbsoluteUri.ToString()
                };
            });
        }

        private async Task<string> ExtractIFrameCode(ContentProviderHttpRequest request)
        {
            var response = await Http.GetAsync(request.RequestUri);
using (var responseStream = await response.Content.ReadAsStreamAsync())
            {
using (var sr = new StreamReader(responseStream))
                {
                    var iframeStr = await sr.ReadToEndAsync();

                    var matches = _extractEmbedCodeRegex.Match(iframeStr)
                                        .Groups
                                        .Cast<Group>()
                                        .Skip(1)
                                        .Select(g => g.Value)
                                        .Where(v => !String.IsNullOrEmpty(v));

                    return matches.FirstOrDefault() ?? String.Empty;
                }
            }
        }

        public override bool IsValidContent(Uri uri)
        {
            return uri.AbsoluteUri.StartsWith("http://ustream.tv/", StringComparison.OrdinalIgnoreCase)
               || uri.AbsoluteUri.StartsWith("http://www.ustream.tv/", StringComparison.OrdinalIgnoreCase);
        }
    }
}