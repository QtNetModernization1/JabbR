using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Ninject;
using Ninject.Syntax;


namespace JabbR.Infrastructure
{
    public class NinjectDependencyScope : IServiceScope
    {
        private IResolutionRoot resolver;

        internal NinjectDependencyScope(IResolutionRoot resolver)
        {
            Contract.Assert(resolver != null);

            this.resolver = resolver;
        }

        public void Dispose()
        {
            IDisposable disposable = resolver as IDisposable;
            if (disposable != null)
                disposable.Dispose();

            resolver = null;
        }

        public IServiceProvider ServiceProvider => new NinjectServiceProvider(resolver);

        private class NinjectServiceProvider : IServiceProvider
        {
            private readonly IResolutionRoot _resolver;

            public NinjectServiceProvider(IResolutionRoot resolver)
            {
                _resolver = resolver;
            }

            public object GetService(Type serviceType)
            {
                if (_resolver == null)
                    throw new ObjectDisposedException("this", "This scope has already been disposed");

                return _resolver.TryGet(serviceType);
            }
        }
    }

    public class NinjectWebApiDependencyResolver : IServiceScopeFactory
    {
        private IKernel kernel;

        public NinjectWebApiDependencyResolver(IKernel kernel)
        {
            this.kernel = kernel;
        }

        public IServiceScope CreateScope()
        {
            return new NinjectDependencyScope(kernel.BeginBlock());
        }
    }
}