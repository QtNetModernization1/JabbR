using Microsoft.Ajax.Utilities;
using Microsoft.AspNetCore.SignalR;

namespace JabbR.Infrastructure
{
    public class JavaScriptMinifier
    {
        public string Minify(string source)
        {
            return new Minifier().MinifyJavaScript(source);
        }
    }
}