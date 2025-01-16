using Microsoft.Ajax.Utilities;

namespace JabbR.Infrastructure
{
    public class AjaxMinMinifier
    {
        public string MinifyJavaScript(string source)
        {
            return new Minifier().MinifyJavaScript(source);
        }
    }
}