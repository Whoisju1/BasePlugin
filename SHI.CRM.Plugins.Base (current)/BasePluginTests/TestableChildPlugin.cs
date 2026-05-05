using System;
using Microsoft.Xrm.Sdk;
using SHI.CRM.Plugins.Base;
using SHI.CRM.Plugins.Base.Infrastructure;
using SHI.CRM.Plugins.Base.Telemetry;


namespace BasePluginTests
{
    /// <summary>
    /// Test double for BasePlugin that surfaces the abstract ExecutePluginLogic for assertions.
    /// </summary>
    internal class TestableChildPlugin : BasePlugin
    {
        private readonly Action<IServiceProvider, PluginServices, ITracingService> _executeAction;

        public TestableChildPlugin() { }

        /// <summary>
        /// Allows injecting a delegate to observe the parameters passed into ExecutePluginLogic.
        /// </summary>
        public TestableChildPlugin(Action<IServiceProvider, PluginServices, ITracingService> executeAction)
        {
            _executeAction = executeAction;
        }

        /// <summary>
        /// Creates an instance that will throw the provided exception when executed.
        /// </summary>
        public static TestableChildPlugin ThatThrows(Exception exception) =>
            new() { ExceptionToThrow = exception };

        public Exception ExceptionToThrow { get; init; }

        /// <summary>
        /// When set, <see cref="BasePlugin.ResolveTelemetry"/> returns this instance instead of the singleton.
        /// </summary>
        public TelemetryAdapter TelemetryOverride { get; init; }

        internal override TelemetryAdapter ResolveTelemetry(IOrganizationService orgService) =>
            TelemetryOverride ?? base.ResolveTelemetry(orgService);

        protected override void ExecutePluginLogic(
            IServiceProvider serviceProvider,
            PluginServices services,
            ITracingService cloudTracing)
        {
            if (ExceptionToThrow is not null)
                throw ExceptionToThrow;

            _executeAction?.Invoke(serviceProvider, services, cloudTracing);
        }
    }
}
