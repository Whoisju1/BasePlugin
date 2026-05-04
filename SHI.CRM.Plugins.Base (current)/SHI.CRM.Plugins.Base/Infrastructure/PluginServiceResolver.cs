using System;
using Microsoft.Xrm.Sdk;

namespace SHI.CRM.Plugins.Base.Infrastructure
{
    /// <summary>
    /// Resolves common CRM plugin services and enforces required dependencies.
    /// The execution service uses the step run-as identity, while the permission-check service uses the initiating
    /// caller identity so derived plug-ins can make the distinction explicitly.
    /// </summary>
    public static class PluginServiceResolver
    {
        /// <summary>
        /// Resolves the services required for a plug-in invocation.
        /// </summary>
        /// <param name="serviceProvider">Platform service provider supplied by Dataverse.</param>
        /// <returns>A <see cref="PluginServices"/> container for the current invocation.</returns>
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

            var executionService = serviceFactory.CreateOrganizationService(context.UserId);
            // Avoid creating a second proxy when the initiating caller and execution identity are already the same.
            var permissionCheckService = context.InitiatingUserId == Guid.Empty
                || context.InitiatingUserId == context.UserId
                ? executionService
                : serviceFactory.CreateOrganizationService(context.InitiatingUserId);

            return new PluginServices(
                tracing,
                context,
                executionService,
                permissionCheckService
            );
        }
    }
}
