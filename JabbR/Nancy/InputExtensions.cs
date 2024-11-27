using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Nancy;
using Nancy.ViewEngines.Razor;

namespace JabbR
{
    public class HtmlHelpers<TModel>
    {
        public ModelStateDictionary ModelState { get; }

        public HtmlHelpers(ModelStateDictionary modelState)
        {
            ModelState = modelState;
        }

        public IEnumerable<string> GetErrorsForProperty(string propertyName)
        {
            if (ModelState.TryGetValue(propertyName, out var entry) && entry.Errors.Any())
            {
                return entry.Errors.Select(e => e.ErrorMessage);
            }
            return Enumerable.Empty<string>();
        }
    }

    public static class InputExtensions
    {
        public static string TextBox<TModel>(this HtmlHelpers<TModel> htmlHelper, string propertyName)
        {
            return TextBox(htmlHelper, propertyName, String.Empty);
        }

        public static string TextBox<TModel>(this HtmlHelpers<TModel> htmlHelper, string propertyName, string className)
        {
            return TextBox(htmlHelper, propertyName, className, null);
        }

        public static string TextBox<TModel>(this HtmlHelpers<TModel> htmlHelper, string propertyName, string className, string placeholder)
        {
            return InputHelper(htmlHelper, "text", propertyName, htmlHelper.GetValueForProperty(propertyName), className, placeholder);
        }

        public static string Password<TModel>(this HtmlHelpers<TModel> htmlHelper, string propertyName)
        {
            return Password(htmlHelper, propertyName, String.Empty);
        }

        public static string Password<TModel>(this HtmlHelpers<TModel> htmlHelper, string propertyName, string className)
        {
            return Password(htmlHelper, propertyName, className, null);
        }

        public static string Password<TModel>(this HtmlHelpers<TModel> htmlHelper, string propertyName, string className, string placeholder)
        {
            return InputHelper(htmlHelper, "password", propertyName, null, className, placeholder);
        }

        private const string InputTemplate = @"<input type=""{0}"" id=""{1}"" name=""{2}"" value=""{3}"" class=""{4}"" placeholder=""{5}"" />";
        private static string InputHelper<TModel>(HtmlHelpers<TModel> htmlHelper, string inputType, string propertyName, string value, string className, string placeholder)
        {
            bool hasError = htmlHelper.GetErrorsForProperty(propertyName).Any();

            return String.Format(InputTemplate, inputType, propertyName, propertyName, value, hasError ? String.Format("{0} {1}", className, "error").Trim() : className, placeholder);
        }

        internal static string GetValueForProperty<TModel>(this HtmlHelpers<TModel> htmlHelper, string propertyName)
        {
            var propInfo =
                typeof (TModel).GetProperties()
                               .FirstOrDefault(
                                   x => x.Name.Equals(propertyName, StringComparison.InvariantCultureIgnoreCase));

            string value = null;

            if (propInfo != null && htmlHelper.Model != null)
            {
                value = propInfo.GetValue(htmlHelper.Model) as string;
            }

            if (String.IsNullOrWhiteSpace(value))
            {
                value = htmlHelper.RenderContext.Context.Request.Form[propertyName];
            }

            if (String.IsNullOrWhiteSpace(value))
            {
                value = htmlHelper.RenderContext.Context.Request.Query[propertyName];
            }

            return value;
        }
    }
}