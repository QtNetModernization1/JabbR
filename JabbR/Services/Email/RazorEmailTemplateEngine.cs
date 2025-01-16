using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Text.RegularExpressions;
using JabbR.Infrastructure;

namespace JabbR.Services
{
    public class SimpleEmailTemplateEngine : IEmailTemplateEngine
    {
        public const string DefaultSharedTemplateSuffix = "";
        public const string DefaultHtmlTemplateSuffix = "html";
        public const string DefaultTextTemplateSuffix = "text";

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
            _contentReader = contentReader ?? throw new ArgumentNullException(nameof(contentReader));
            _sharedTemplateSuffix = sharedTemplateSuffix;
            _htmlTemplateSuffix = htmlTemplateSuffix;
            _textTemplateSuffix = textTemplateSuffix;
            _templateSuffixes = new Dictionary<string, string>
            {
                { _sharedTemplateSuffix, string.Empty },
                { _htmlTemplateSuffix, ContentTypes.Html },
                { _textTemplateSuffix, ContentTypes.Text }
            };
        }

        public Email RenderTemplate(string templateName, object model = null)
        {
            if (string.IsNullOrWhiteSpace(templateName))
            {
                throw new ArgumentException($"{nameof(templateName)} cannot be blank.", nameof(templateName));
            }

            var email = new Email();
            var dynamicModel = CreateModel(model);

            foreach (var suffix in _templateSuffixes)
            {
                var content = _contentReader.Read(templateName, suffix.Key);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    var renderedContent = RenderTemplate(content, dynamicModel);
                    ApplyTemplateToEmail(email, suffix.Value, renderedContent);
                }
            }

            return email;
        }

        private string RenderTemplate(string template, dynamic model)
        {
            return Regex.Replace(template, @"@Model\.(\w+)", match =>
            {
                string propertyName = match.Groups[1].Value;
                if (model != null && ((IDictionary<string, object>)model).TryGetValue(propertyName, out object value))
                {
                    return value?.ToString() ?? string.Empty;
                }
                return match.Value;
            });
        }

        private void ApplyTemplateToEmail(Email email, string contentType, string content)
        {
            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var headers = new Dictionary<string, string>();
            var body = new List<string>();
            bool isBody = false;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) && !isBody)
                {
                    isBody = true;
                    continue;
                }

                if (!isBody)
                {
                    var parts = line.Split(new[] { ':' }, 2);
                    if (parts.Length == 2)
                    {
                        headers[parts[0].Trim()] = parts[1].Trim();
                    }
                }
                else
                {
                    body.Add(line);
                }
            }

            if (headers.TryGetValue("To", out string to))
            {
                email.To.AddRange(to.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim()));
            }
            if (headers.TryGetValue("From", out string from))
            {
                email.From = from.Trim();
            }
            if (headers.TryGetValue("Subject", out string subject))
            {
                email.Subject = subject.Trim();
            }
            if (headers.TryGetValue("Cc", out string cc))
            {
                email.CC.AddRange(cc.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim()));
            }
            if (headers.TryGetValue("Bcc", out string bcc))
            {
                email.Bcc.AddRange(bcc.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim()));
            }

            var bodyContent = string.Join(Environment.NewLine, body);
            if (contentType == ContentTypes.Html)
            {
                email.HtmlBody = bodyContent;
            }
            else if (contentType == ContentTypes.Text)
            {
                email.TextBody = bodyContent;
            }
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

            var compilableTemplates = templates.Select(x => new KeyValuePair<string, string>(x.TemplateName, x.Content)).ToArray();
            var assembly = GenerateAssembly(compilableTemplates);

            return templates.Select(x => new { ContentType = x.ContentType, Type = assembly.GetType(NamespaceName + "." + x.TemplateName, true, false) })
                            .ToDictionary(k => k.ContentType, e => e.Type);
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

        private static Assembly GenerateAssembly(params KeyValuePair<string, string>[] templates)
        {
            var templateResults = templates.Select(pair => _razorEngine.GenerateCode(new StringReader(pair.Value), pair.Key, NamespaceName, pair.Key + ".cs")).ToList();

            if (templateResults.Any(result => result.ParserErrors.Any()))
            {
                var parseExceptionMessage = String.Join(Environment.NewLine + Environment.NewLine, templateResults.SelectMany(r => r.ParserErrors).Select(e => e.Location + ":" + Environment.NewLine + e.Message).ToArray());

                throw new InvalidOperationException(parseExceptionMessage);
            }

            using (var codeProvider = new CSharpCodeProvider())
            {
                var compilerParameter = new CompilerParameters(_referencedAssemblies)
                                            {
                                                IncludeDebugInformation = false,
                                                GenerateInMemory = true,
                                                CompilerOptions = "/optimize"
                                            };

                var compilerResults = codeProvider.CompileAssemblyFromDom(compilerParameter, templateResults.Select(r => r.GeneratedCode).ToArray());

                if (compilerResults.Errors.HasErrors)
                {
                    var compileExceptionMessage = String.Join(Environment.NewLine + Environment.NewLine, compilerResults.Errors.OfType<CompilerError>().Where(ce => !ce.IsWarning).Select(e => e.FileName + ":" + Environment.NewLine + e.ErrorText).ToArray());

                    throw new InvalidOperationException(compileExceptionMessage);
                }

                return compilerResults.CompiledAssembly;
            }
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