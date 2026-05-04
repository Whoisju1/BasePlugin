using System;
using Microsoft.Xrm.Sdk;
using SHI.CRM.Plugins.Base;


namespace BasePluginTests
{
    /// <summary>
    /// Test double for BasePlugin that surfaces the abstract ExecutePluginLogic for assertions.
    /// </summary>
    internal class TestableChildPlugin : BasePlugin
    {
        private readonly Action<IServiceProvider, IPluginExecutionContext, IOrganizationService, ITracingService, ITracingService> _executeAction;

        public TestableChildPlugin() { }

        /// <summary>
        /// Allows injecting a delegate to observe the parameters passed into ExecutePluginLogic.
        /// </summary>
        public TestableChildPlugin(Action<IServiceProvider, IPluginExecutionContext, IOrganizationService, ITracingService, ITracingService> executeAction)
        {
            _executeAction = executeAction;
        }

        /// <summary>
        /// Creates an instance that will throw the provided exception when executed.
        /// </summary>
        public static TestableChildPlugin ThatThrows(Exception exception) =>
            new() { ExceptionToThrow = exception };

        public Exception ExceptionToThrow { get; init; }

        protected override void ExecutePluginLogic(
            IServiceProvider serviceProvider,
            IPluginExecutionContext context,
            IOrganizationService orgService,
            ITracingService tracing,
            ITracingService cloudTracing)
        {
            if (ExceptionToThrow is not null)
                throw ExceptionToThrow;

            _executeAction?.Invoke(serviceProvider, context, orgService, tracing, cloudTracing);
        }
    }
}
