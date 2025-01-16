using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.SignalR;
using Ninject;

namespace JabbR.Infrastructure
{
    internal class NinjectSignalRDependencyResolver : IHubActivator
    {
        private readonly IKernel _kernel;

        public NinjectSignalRDependencyResolver(IKernel kernel)
        {
            _kernel = kernel;
        }

        public HubLifetimeContext Create(HubActivatorContext context)
        {
            return new HubLifetimeContext(context.Context, _kernel.Get(context.HubType), (hub, isDisposable) =>
            {
                if (isDisposable)
                {
                    _kernel.Release(hub);
                }
            });
        }

        public T GetService<T>() where T : class
        {
            return _kernel.TryGet<T>();
        }

        public IEnumerable<T> GetServices<T>() where T : class
        {
            return _kernel.GetAll<T>();
        }
    }
}