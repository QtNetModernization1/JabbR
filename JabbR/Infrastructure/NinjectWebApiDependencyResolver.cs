using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Ninject;
using Ninject.Syntax;


namespace JabbR.Infrastructure
{
    public class NinjectDependencyScope : IServiceProvider, IDisposable
    {
        private IResolutionRoot resolver;

        internal NinjectDependencyScope(IResolutionRoot resolver)
        {
            Contract.Assert(resolver != null);

            this.resolver = resolver;
        }

        public void Dispose()
        {
            if (resolver is IDisposable disposable)
                disposable.Dispose();

            resolver = null;
        }

        public object GetService(Type serviceType)
        {
            if (resolver == null)
                throw new ObjectDisposedException("this", "This scope has already been disposed");

            return resolver.TryGet(serviceType);
        }

        public IEnumerable<object> GetServices(Type serviceType)
        {
            if (resolver == null)
                throw new ObjectDisposedException("this", "This scope has already been disposed");

            return resolver.GetAll(serviceType);
        }
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
            return new NinjectServiceScope(kernel.BeginBlock());
        }
    }

    public class NinjectServiceScope : IServiceScope
    {
        private readonly NinjectDependencyScope _scope;

        public NinjectServiceScope(IResolutionRoot resolver)
        {
            _scope = new NinjectDependencyScope(resolver);
        }

        public IServiceProvider ServiceProvider => _scope;

        public void Dispose()
        {
            _scope.Dispose();
        }
    }
}