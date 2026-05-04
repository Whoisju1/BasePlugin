using Microsoft.Xrm.Sdk;

namespace SHI.CRM.Plugins.Base.Infrastructure
{
    /// <summary>
    /// Aggregates CRM plugin services resolved from the service provider.
    /// </summary>
    public sealed class PluginServices
    {
        public PluginServices(
            ITracingService tracing,
            IPluginExecutionContext context,
            IOrganizationService organizationService
        )
        {
            Tracing = tracing;
            Context = context;
            OrganizationService = organizationService;
        }

        public ITracingService Tracing { get; }
        public IPluginExecutionContext Context { get; }
        public IOrganizationService OrganizationService { get; }
    }
}
