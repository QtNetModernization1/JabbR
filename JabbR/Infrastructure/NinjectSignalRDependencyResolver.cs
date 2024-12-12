using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.SignalR;
using Ninject;

namespace JabbR.Infrastructure
{
    internal class NinjectSignalRDependencyResolver : IHubActivator<Hub>
    {
        private readonly IKernel _kernel;
        public NinjectSignalRDependencyResolver(IKernel kernel)
        {
            _kernel = kernel;
        }

        public Hub Create(HubActivatorContext context)
        {
            return (Hub)_kernel.Get(context.HubType);
        }

        public void Release(Hub hub)
        {
            // Ninject will handle the disposal of the hub
        }
    }
}