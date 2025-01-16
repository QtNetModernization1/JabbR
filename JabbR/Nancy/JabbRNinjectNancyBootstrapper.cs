using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;

using Nancy;
using Nancy.Bootstrapper;
using Nancy.Configuration;
using Nancy.Owin;
using Nancy.Security;
using Nancy.Bootstrappers.Ninject;

using Ninject;
using Ninject.Extensions.ChildKernel;

using Microsoft.AspNetCore.Http;

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

        protected override INancyEnvironmentConfigurator GetEnvironmentConfigurator()
        {
            return environment =>
            {
                environment.Tracing(enabled: false, displayErrorTraces: true);
                return environment;
            };
        }

        protected override void ApplicationStartup(IKernel container, IPipelines pipelines)
        {
            base.ApplicationStartup(container, pipelines);

            Csrf.Enable(pipelines);

            pipelines.BeforeRequest.AddItemToStartOfPipeline(FlowPrincipal);
            pipelines.BeforeRequest.AddItemToStartOfPipeline(SetCulture);
        }

        protected override void RegisterNancyEnvironment(IKernel container, INancyEnvironment environment)
        {
            // Register the Nancy environment with the container
            container.Bind<INancyEnvironment>().ToConstant(environment).InSingletonScope();

            // Configure any environment settings if needed
            environment.Tracing(enabled: false, displayErrorTraces: true);
        }

        public override INancyEnvironment GetEnvironment()
        {
            var environment = new DefaultNancyEnvironment();
            RegisterNancyEnvironment(GetApplicationContainer(), environment);
            return environment;
        }

        private Response FlowPrincipal(NancyContext context)
        {
            if (context.Items.TryGetValue("OWIN_REQUEST_ENVIRONMENT", out var envObj) && envObj is IDictionary<string, object> env)
            {
                if (env.TryGetValue("server.User", out var userObj) && userObj is ClaimsPrincipal principal)
                {
                    context.CurrentUser = new ClaimsPrincipalUserIdentity(principal);
                }

                if (env.TryGetValue("host.AppMode", out var appModeObj) && appModeObj is string appMode)
                {
                    context.Items["_debugMode"] = !string.IsNullOrEmpty(appMode) &&
                        appMode.Equals("development", StringComparison.OrdinalIgnoreCase);
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

        // Remove the Get<T> method as it's no longer needed
    }
}