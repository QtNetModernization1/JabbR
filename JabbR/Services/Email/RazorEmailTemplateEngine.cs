using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using JabbR.Infrastructure;

namespace JabbR.Services
{
    public class SimpleEmailTemplateEngine : IEmailTemplateEngine
    {
        public const string DefaultSharedTemplateSuffix = "";
        public const string DefaultHtmlTemplateSuffix = "html";
        public const string DefaultTextTemplateSuffix = "text";

        private const string NamespaceName = "JabbR.Views.EmailTemplates";

        private readonly IEmailTemplateContentReader _contentReader;
        private readonly string _sharedTemplateSuffix;
        private readonly string _htmlTemplateSuffix;
        private readonly string _textTemplateSuffix;
        private readonly IDictionary<string, string> _templateSuffixes;

        public SimpleEmailTemplateEngine(IEmailTemplateContentReader contentReader)
            : this(contentReader, DefaultSharedTemplateSuffix, DefaultHtmlTemplateSuffix, DefaultTextTemplateSuffix)
        {
        }

        public SimpleEmailTemplateEngine(IEmailTemplateContentReader contentReader, string sharedTemplateSuffix, string htmlTemplateSuffix, string textTemplateSuffix)
        {
            if (contentReader == null)
            {
                throw new ArgumentNullException("contentReader");
            }

            _contentReader = contentReader;
            _sharedTemplateSuffix = sharedTemplateSuffix;
            _htmlTemplateSuffix = htmlTemplateSuffix;
            _textTemplateSuffix = textTemplateSuffix;
            _templateSuffixes = new Dictionary<string, string>
                                {
                                    { _sharedTemplateSuffix, String.Empty },
                                    { _htmlTemplateSuffix, ContentTypes.Html },
                                    { _textTemplateSuffix, ContentTypes.Text }
                                };
        }

        public Email RenderTemplate(string templateName, object model = null)
        {
            if (string.IsNullOrWhiteSpace(templateName))
            {
                throw new ArgumentException("Template name cannot be blank.", nameof(templateName));
            }

            var email = new Email();
            var expandoModel = CreateModel(model);

            foreach (var suffix in _templateSuffixes)
            {
                var content = _contentReader.Read(templateName, suffix.Key);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    var renderedContent = RenderTemplate(content, expandoModel);
                    ApplyRenderedContent(email, suffix.Value, renderedContent);
                }
            }

            return email;
        }

        private string RenderTemplate(string template, dynamic model)
        {
            return Regex.Replace(template, @"\{\{(.+?)\}\}", match =>
            {
                string propertyName = match.Groups[1].Value.Trim();
                return GetPropertyValue(model, propertyName)?.ToString() ?? string.Empty;
            });
        }

        private object GetPropertyValue(dynamic obj, string propertyName)
        {
            if (obj is IDictionary<string, object> dict)
            {
                return dict.TryGetValue(propertyName, out var value) ? value : null;
            }
            return null;
        }

        private void ApplyRenderedContent(Email email, string contentType, string content)
        {
            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                if (line.StartsWith("To:", StringComparison.OrdinalIgnoreCase))
                    email.To.Add(line.Substring(3).Trim());
                else if (line.StartsWith("From:", StringComparison.OrdinalIgnoreCase))
                    email.From = line.Substring(5).Trim();
                else if (line.StartsWith("Subject:", StringComparison.OrdinalIgnoreCase))
                    email.Subject = line.Substring(8).Trim();
                else if (line.StartsWith("Cc:", StringComparison.OrdinalIgnoreCase))
                    email.CC.Add(line.Substring(3).Trim());
                else if (line.StartsWith("Bcc:", StringComparison.OrdinalIgnoreCase))
                    email.Bcc.Add(line.Substring(4).Trim());
                else
                {
                    if (contentType == ContentTypes.Html)
                        email.HtmlBody += line + "\n";
                    else if (contentType == ContentTypes.Text)
                        email.TextBody += line + "\n";
                }
            }
        }

        private static dynamic CreateModel(object model)
        {
            if (model == null)
            {
                return new ExpandoObject();
            }

            if (model is IDynamicMetaObjectProvider)
            {
                return model;
            }

            var expandoObj = new ExpandoObject();
            var expandoDict = (IDictionary<string, object>)expandoObj;

            foreach (var prop in model.GetType().GetProperties())
            {
                expandoDict[prop.Name] = prop.GetValue(model);
            }

            return expandoObj;
        }

        private static dynamic CreateModel(object model)
        {
            if (model == null)
            {
                return null;
            }

            if (model is IDynamicMetaObjectProvider)
            {
                return model;
            }

            var propertyMap = model.GetType()
                                   .GetProperties()
                                   .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
                                   .ToDictionary(property => property.Name, property => property.GetValue(model, null));

            return new DynamicModel(propertyMap);
        }

    }
}