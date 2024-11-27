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

    public object GetService(Type serviceType)
    {
        if (resolver == null)
            throw new ObjectDisposedException("this", "This scope has already been disposed");

        return resolver.TryGet(serviceType);
    }

    public IServiceProvider ServiceProvider => this;
    }

public class NinjectWebApiDependencyResolver : NinjectDependencyScope, IServiceProvider
    {
        private IKernel kernel;

        public NinjectWebApiDependencyResolver(IKernel kernel)
            : base(kernel)
        {
            this.kernel = kernel;
        }

public IServiceScope CreateScope()
        {
            return new NinjectDependencyScope(kernel.BeginBlock());
        }
    }
}