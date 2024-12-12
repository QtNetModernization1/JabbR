using System;
using System.Linq;
using System.Web;

namespace JabbR
{
    public static class InputExtensions
    {
        public static IHtmlString TextBox(string propertyName)
        {
            return TextBox(propertyName, String.Empty);
        }

        public static IHtmlString TextBox(string propertyName, string className)
        {
            return TextBox(propertyName, className, null);
        }

        public static IHtmlString TextBox(string propertyName, string className, string placeholder)
        {
            return InputHelper("text", propertyName, null, className, placeholder);
        }

        public static IHtmlString Password(string propertyName)
        {
            return Password(propertyName, String.Empty);
        }

        public static IHtmlString Password(string propertyName, string className)
        {
            return Password(propertyName, className, null);
        }

        public static IHtmlString Password(string propertyName, string className, string placeholder)
        {
            return InputHelper("password", propertyName, null, className, placeholder);
        }

        private const string InputTemplate = @"<input type=""{0}"" id=""{1}"" name=""{2}"" value=""{3}"" class=""{4}"" placeholder=""{5}"" />";
        private static IHtmlString InputHelper(string inputType, string propertyName, string value, string className, string placeholder)
        {
            return new HtmlString(String.Format(InputTemplate, inputType, propertyName, propertyName, value ?? "", className ?? "", placeholder ?? ""));
        }
    }
}