using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;

using Nancy;
using Nancy.Bootstrapper;
using Nancy.Bootstrappers.Ninject;
using Nancy.Configuration;
using Microsoft.AspNetCore.Http;
using Nancy.Security;

using Ninject;
using Nancy.Configuration;

namespace JabbR.Nancy
{
    public class JabbRNinjectNancyBootstrapper : NinjectNancyBootstrapper
    {
        private readonly IKernel _kernel;

        public JabbRNinjectNancyBootstrapper(IKernel kernel)
        {
            _kernel = kernel;
        }

        protected override IKernel GetApplicationContainer()
        {
            return _kernel;
        }

        protected override INancyEnvironment GetEnvironment()
        {
            return new DefaultNancyEnvironment();
        }

        protected override void ConfigureApplicationContainer(IKernel existingContainer)
        {
            // Configure your container here if needed
            base.ConfigureApplicationContainer(existingContainer);
        }

        public override void Configure(INancyEnvironment environment)
        {
            base.Configure(environment);
            // Add any additional configuration here
        }

        protected override void RegisterNancyEnvironment(IKernel container, INancyEnvironment environment)
        {
            // Register the Nancy environment
            container.Bind<INancyEnvironment>().ToConstant(environment).InSingletonScope();
        }

        protected override void ApplicationStartup(IKernel container, IPipelines pipelines)
        {
            base.ApplicationStartup(container, pipelines);

            Csrf.Enable(pipelines);

            pipelines.BeforeRequest.AddItemToStartOfPipeline(FlowPrincipal);
            pipelines.BeforeRequest.AddItemToStartOfPipeline(SetCulture);
        }

        private Response FlowPrincipal(NancyContext context)
        {
            var env = Get<IDictionary<string, object>>(context.Items, "owin.Environment");
            if (env != null)
            {
                var principal = Get<IPrincipal>(env, "server.User") as ClaimsPrincipal;
                if (principal != null)
                {
                    context.CurrentUser = principal;
                }

                var appMode = Get<string>(env, "host.AppMode");

                if (!String.IsNullOrEmpty(appMode) &&
                    appMode.Equals("development", StringComparison.OrdinalIgnoreCase))
                {
                    context.Items["_debugMode"] = true;
                }
                else
                {
                    context.Items["_debugMode"] = false;
                }
            }

            return null;
        }

        private Response SetCulture(NancyContext ctx)
        {
            Thread.CurrentThread.CurrentCulture = ctx.Culture;
            Thread.CurrentThread.CurrentUICulture = ctx.Culture;
            return null;
        }

        private static T Get<T>(IDictionary<string, object> env, string key)
        {
            object value;
            if (env.TryGetValue(key, out value))
            {
                return (T)value;
            }
            return default(T);
        }

        protected override INancyEnvironmentConfigurator GetEnvironmentConfigurator()
        {
            return new DefaultNancyEnvironmentConfigurator(
                GetApplicationContainer().Get<INancyEnvironmentFactory>(),
                GetApplicationContainer().GetAll<INancyDefaultConfigurationProvider>());
        }
    }
}