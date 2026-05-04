using Microsoft.Xrm.Sdk;

namespace SHI.CRM.Plugins.Base.Infrastructure
{
    /// <summary>
    /// Aggregates the services resolved for a single CRM plug-in invocation.
    /// Keeps execution-time and permission-check organization services separate so callers can choose the correct
    /// identity for each operation.
    /// </summary>
    public sealed class PluginServices
    {
        public PluginServices(
            ITracingService tracing,
            IPluginExecutionContext context,
            IOrganizationService executionService,
            IOrganizationService permissionCheckService
        )
        {
            Tracing = tracing;
            Context = context;
            ExecutionService = executionService;
            PermissionCheckService = permissionCheckService;
        }

        /// <summary>
        /// Platform tracing service for the current invocation.
        /// </summary>
        public ITracingService Tracing { get; }

        /// <summary>
        /// Dataverse execution context describing the current pipeline invocation.
        /// </summary>
        public IPluginExecutionContext Context { get; }

        /// <summary>
        /// Organization service created with <c>context.UserId</c> for the step execution identity.
        /// Use this for the work the plug-in step is configured to perform.
        /// </summary>
        public IOrganizationService ExecutionService { get; }

        /// <summary>
        /// Organization service created with <c>context.InitiatingUserId</c> for permission-sensitive reads and checks.
        /// When the initiating caller and execution identity are the same, this reuses <see cref="ExecutionService"/>.
        /// </summary>
        public IOrganizationService PermissionCheckService { get; }
    }
}
