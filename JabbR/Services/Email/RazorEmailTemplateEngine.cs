using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using JabbR.Infrastructure;
using Microsoft.CSharp;
using RazorEngine;
using RazorEngine.Templating;

namespace JabbR.Services
{
    public class RazorEmailTemplateEngine : IEmailTemplateEngine
    {
        public const string DefaultSharedTemplateSuffix = "";
        public const string DefaultHtmlTemplateSuffix = "html";
        public const string DefaultTextTemplateSuffix = "text";

        private const string NamespaceName = "JabbR.Views.EmailTemplates";

        private static readonly string[] _referencedAssemblies = BuildReferenceList().ToArray();
        private static readonly IRazorEngineService _razorEngine = CreateRazorEngine();
        private static readonly Dictionary<string, IDictionary<string, Type>> _typeMapping = new Dictionary<string, IDictionary<string, Type>>(StringComparer.OrdinalIgnoreCase);
        private static readonly ReaderWriterLockSlim _syncLock = new ReaderWriterLockSlim();

        private readonly IEmailTemplateContentReader _contentReader;
        private readonly string _sharedTemplateSuffix;
        private readonly string _htmlTemplateSuffix;
        private readonly string _textTemplateSuffix;
        private readonly IDictionary<string, string> _templateSuffixes;

        public RazorEmailTemplateEngine(IEmailTemplateContentReader contentReader)
            : this(contentReader, DefaultSharedTemplateSuffix, DefaultHtmlTemplateSuffix, DefaultTextTemplateSuffix)
        {
            _contentReader = contentReader;
        }

        public RazorEmailTemplateEngine(IEmailTemplateContentReader contentReader, string sharedTemplateSuffix, string htmlTemplateSuffix, string textTemplateSuffix)
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
            if (String.IsNullOrWhiteSpace(templateName))
            {
                throw new System.ArgumentException(String.Format(System.Globalization.CultureInfo.CurrentUICulture, "\"{0}\" cannot be blank.", "templateName"));
            }

            var templates = CreateTemplateInstances(templateName);

            foreach (var pair in templates)
            {
                pair.Value.SetModel(CreateModel(model));
                pair.Value.Execute();
            }

            var mail = new Email();

            templates.SelectMany(x => x.Value.To)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Each(email => mail.To.Add(email));

            templates.SelectMany(x => x.Value.ReplyTo)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Each(email => mail.ReplyTo.Add(email));

            templates.SelectMany(x => x.Value.Bcc)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Each(email => mail.Bcc.Add(email));

            templates.SelectMany(x => x.Value.CC)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Each(email => mail.CC.Add(email));

            IEmailTemplate template = null;

            // text template (.text.cshtml file)
            if (templates.TryGetValue(ContentTypes.Text, out template))
            {
                SetProperties(template, mail, body => { mail.TextBody = body; });
            }
            // html template (.html.cshtml file)
            if (templates.TryGetValue(ContentTypes.Html, out template))
            {
                SetProperties(template, mail, body => { mail.HtmlBody = body; });
            }
            // shared template (.cshtml file)
            if (templates.TryGetValue(String.Empty, out template))
            {
                SetProperties(template, mail, null);
            }

            return mail;
        }

        private IDictionary<string, IEmailTemplate> CreateTemplateInstances(string templateName)
        {
            return GetTemplateTypes(templateName).Select(pair => new { ContentType = pair.Key, Template = (IEmailTemplate)Activator.CreateInstance(pair.Value) })
                                                 .ToDictionary(k => k.ContentType, e => e.Template);
        }

        private IDictionary<string, Type> GetTemplateTypes(string templateName)
        {
            IDictionary<string, Type> templateTypes;

            _syncLock.EnterUpgradeableReadLock();

            try
            {
                if (!_typeMapping.TryGetValue(templateName, out templateTypes))
                {
                    _syncLock.EnterWriteLock();

                    try
                    {
                        templateTypes = GenerateTemplateTypes(templateName);
                        _typeMapping.Add(templateName, templateTypes);
                    }
                    finally
                    {
                        _syncLock.ExitWriteLock();
                    }
                }
            }
            finally
            {
                _syncLock.ExitUpgradeableReadLock();
            }

            return templateTypes;
        }

        private IDictionary<string, Type> GenerateTemplateTypes(string templateName)
        {
            var templates = _templateSuffixes.Select(pair => new
                                                    {
                                                        Suffix = pair.Key,
                                                        TemplateName = templateName + pair.Key,
                                                        Content = _contentReader.Read(templateName, pair.Key),
                                                        ContentType = pair.Value
                                                    })
                                             .Where(x => !String.IsNullOrWhiteSpace(x.Content))
                                             .ToList();

            var result = new Dictionary<string, Type>();
            foreach (var template in templates)
            {
                _razorEngine.AddTemplate(template.TemplateName, template.Content);
                result[template.ContentType] = typeof(EmailTemplate);
            }

            return result;
        }

        private static void SetProperties(IEmailTemplate template, Email mail, Action<string> updateBody)
        {
            if (template != null)
            {
                if (!String.IsNullOrWhiteSpace(template.From))
                {
                    mail.From = template.From;
                }

                if (!String.IsNullOrWhiteSpace(template.Sender))
                {
                    mail.Sender = template.Sender;
                }

                if (!String.IsNullOrWhiteSpace(template.Subject))
                {
                    mail.Subject = template.Subject;
                }

                template.Headers.Each(pair => mail.Headers[pair.Key] = pair.Value);

                if (updateBody != null)
                {
                    updateBody(template.Body);
                }
            }
        }

        // Remove the GenerateAssembly method as it's no longer needed with RazorEngine.NetCore

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

        private static IRazorEngineService CreateRazorEngine()
        {
            return RazorEngineService.Create(new EngineConfig
            {
                BaseTemplateType = typeof(EmailTemplate),
                DefaultNamespace = NamespaceName
            });
        }

        private static IEnumerable<string> BuildReferenceList()
        {
            string currentAssemblyLocation = typeof(RazorEmailTemplateEngine).Assembly.CodeBase.Replace("file:///", String.Empty).Replace("/", "\\");

            return new List<string>
                       {
                           "mscorlib.dll",
                           "system.dll",
                           "system.core.dll",
                           "microsoft.csharp.dll",
                           currentAssemblyLocation
                       };
        }
    }
}