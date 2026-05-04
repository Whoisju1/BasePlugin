using System;
using Microsoft.Xrm.Sdk;

namespace SHI.CRM.Plugins.Base.Infrastructure
{
    /// <summary>
    /// Resolves common CRM plugin services and enforces required dependencies.
    /// </summary>
    public static class PluginServiceResolver
    {
        public static PluginServices Resolve(IServiceProvider serviceProvider)
        {
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var context = (IPluginExecutionContext)
                serviceProvider.GetService(typeof(IPluginExecutionContext));
            var serviceFactory = (IOrganizationServiceFactory)
                serviceProvider.GetService(typeof(IOrganizationServiceFactory));

            if (context == null)
            {
                tracing?.Trace("Plugin execution context is unavailable.");
                throw new InvalidPluginExecutionException(
                    "Plugin execution context is unavailable."
                );
            }

            if (serviceFactory == null)
            {
                tracing?.Trace("Organization service factory is unavailable.");
                throw new InvalidPluginExecutionException(
                    "Organization service factory is unavailable."
                );
            }

            var orgService = serviceFactory.CreateOrganizationService(context.UserId);
            return new PluginServices(tracing, context, orgService);
        }
    }
}
