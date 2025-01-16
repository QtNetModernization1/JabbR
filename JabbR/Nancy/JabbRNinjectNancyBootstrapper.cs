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
            if (context.Environment.ContainsKey("Microsoft.AspNetCore.Http.HttpContext"))
            {
                var httpContext = (HttpContext)context.Environment["Microsoft.AspNetCore.Http.HttpContext"];
                var principal = httpContext.User;
                if (principal != null)
                {
                    context.CurrentUser = new ClaimsPrincipalUserIdentity(principal);
                }

                var appMode = httpContext.RequestServices.GetService(typeof(IHostEnvironment)) as IHostEnvironment;

                if (appMode != null && appMode.IsDevelopment())
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

        // Remove the Get<T> method as it's no longer needed
    }
}