using System;
using System.Diagnostics;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using SHI.CRM.Plugins.Base.Diagnostics;
using SHI.CRM.Plugins.Base.Exceptions;
using SHI.CRM.Plugins.Base.Infrastructure;
using SHI.CRM.Plugins.Base.Telemetry;
using SHI.CRM.Plugins.Base.Validation;

namespace SHI.CRM.Plugins.Base
{
    /// <summary>
    /// Shared base for CRM plugins: resolves core services, traces faults, and delegates execution.
    /// Derived plug-ins receive a <see cref="PluginServices"/> container so execution-time work and
    /// permission-sensitive checks can use different organization service identities when needed.
    /// Uses <see cref="TelemetryTracingService"/> to mirror traces to Application Insights when available.
    /// Telemetry connection string and the inner-trace duplication flag are read from Dataverse
    /// Environment Variables first (<c>shi_ApplicationInsightsConnectionString</c>,
    /// <c>shi_DisableInnerTraceDuplication</c>) with fallback to host environment variables.
    /// </summary>
    public abstract class BasePlugin : IPlugin
    {
        /// <summary>
        /// TTL for the inner-trace duplication flag lookup. Read on every Execute, so caching avoids
        /// a Dataverse round-trip on the hot path while still letting platform admins flip the flag
        /// within the TTL window without a sandbox restart.
        /// </summary>
        internal static readonly TimeSpan DisableTraceFlagCacheTtl = TimeSpan.FromSeconds(60);

        public BasePlugin(string unsecureConfig = null, string secureConfig = null)
        {
            UnsecureConfig = unsecureConfig;
            SecureConfig = secureConfig;
        }

        protected string UnsecureConfig { get; }
        protected string SecureConfig { get; }

        /// <summary>
        /// Entry point invoked by the CRM platform. Resolves dependencies, wires telemetry, delegates to plugin logic, and
        /// converts unexpected exceptions into a user-safe <see cref="InvalidPluginExecutionException"/>.
        /// </summary>
        /// <param name="serviceProvider">Platform service provider supplied by CRM.</param>
        public void Execute(IServiceProvider serviceProvider)
        {
            if (serviceProvider == null)
                throw new ArgumentNullException(nameof(serviceProvider));
            var stopwatch = Stopwatch.StartNew();
            var services = PluginServiceResolver.Resolve(serviceProvider);
            var pluginType = GetType().FullName ?? string.Empty;
            var telemetry = ResolveTelemetry(services.ExecutionService);
            if (!telemetry.IsEnabled)
            {
                TelemetryAdapter.TraceTelemetryDisabledOnce(services.Tracing);
            }
            var commonProps = TelemetryAdapter.BuildCommonProperties(services.Context, pluginType);

            var inputCount = services.Context?.InputParameters?.Count ?? 0;
            var sharedCount = services.Context?.SharedVariables?.Count ?? 0;

            // Default to duplicating traces unless explicitly disabled via Dataverse env var
            // (shi_DisableInnerTraceDuplication) or host environment variable.
            var duplicateInnerTrace = ShouldDuplicateInnerTrace(services.ExecutionService);

            commonProps["TraceDuplicationEnabled"] = duplicateInnerTrace.ToString();
            commonProps["TelemetryEnabled"] = telemetry.IsEnabled.ToString();

            var cloudTracing = new TelemetryTracingService(
                services.Tracing,
                telemetry,
                commonProps,
                duplicateInnerTrace
            );

            if (telemetry.IsEnabled)
            {
                if (inputCount > 0)
                {
                    telemetry.TrackMetric("InputParameterCount", inputCount, commonProps);
                }

                if (sharedCount > 0)
                {
                    telemetry.TrackMetric("SharedVariableCount", sharedCount, commonProps);
                }
            }

            telemetry.TrackTrace("BasePlugin.Execute start", commonProps);

            try
            {
                ExecutePluginLogic(serviceProvider, services, cloudTracing);
            }
            catch (InvalidPluginExecutionException ex)
            {
                telemetry.TrackException(ex, commonProps);
                services.Tracing.TraceWithContext(
                    services.Context,
                    ex,
                    "Business exception",
                    pluginType
                );
                throw;
            }
            catch (FaultException<OrganizationServiceFault> faultEx)
            {
                telemetry.TrackException(
                    faultEx,
                    TelemetryAdapter.CloneWith(
                        commonProps,
                        "ErrorCode",
                        faultEx.Detail != null ? faultEx.Detail.ErrorCode.ToString() : null
                    )
                );
                services.Tracing.TraceWithContext(
                    services.Context,
                    faultEx,
                    "OrganizationService fault",
                    pluginType
                );
                throw CreateUserSafeException(faultEx, services.Context);
            }
            catch (Exception ex)
            {
                telemetry.TrackException(ex, commonProps);
                services.Tracing.TraceWithContext(
                    services.Context,
                    ex,
                    "Unhandled plugin exception",
                    pluginType
                );
                throw CreateUserSafeException(ex, services.Context);
            }
            finally
            {
                stopwatch.Stop();
                var completedProps = TelemetryAdapter.CloneWith(
                    commonProps,
                    "TotalDurationMs",
                    stopwatch.ElapsedMilliseconds.ToString()
                );

                telemetry.TrackTrace("BasePlugin.Execute completed", completedProps);
                telemetry.TrackMetric(
                    "TotalDurationMs",
                    stopwatch.Elapsed.TotalMilliseconds,
                    completedProps
                );
                telemetry.Flush();
            }
        }

        /// <summary>
        /// Executes the derived plug-in business logic using the resolved services for the current invocation.
        /// </summary>
        /// <param name="serviceProvider">Original platform service provider for the current plug-in execution.</param>
        /// <param name="services">
        /// Resolved services for the invocation. Use <see cref="PluginServices.ExecutionService"/> for the work the
        /// step is configured to perform and <see cref="PluginServices.PermissionCheckService"/> when checks must honor
        /// the initiating caller.
        /// </param>
        /// <param name="cloudTracing">
        /// Tracing wrapper that mirrors messages to Application Insights when configured while still writing to the
        /// platform tracing service.
        /// </param>
        protected abstract void ExecutePluginLogic(
            IServiceProvider serviceProvider,
            PluginServices services,
            ITracingService cloudTracing
        );

        protected static T GetInputParameter<T>(IPluginExecutionContext context, string key) =>
            ContextInputExtensions.GetInputParameter<T>(context, key);

        protected static T GetRequiredInputParameter<T>(
            IPluginExecutionContext context,
            string key
        ) => ContextInputExtensions.GetRequiredInputParameter<T>(context, key);

        protected static InvalidPluginExecutionException CreateUserSafeException(
            Exception ex,
            IPluginExecutionContext context
        ) => PluginExceptionFactory.CreateUserSafeException(ex, context);

        /// <summary>
        /// Resolves the telemetry adapter for this invocation. Default implementation returns the process-wide
        /// singleton from <see cref="TelemetryAdapter.GetOrCreate"/>. Override to inject a stub adapter in tests
        /// or to customize telemetry resolution per derived plug-in.
        /// </summary>
        internal virtual TelemetryAdapter ResolveTelemetry(IOrganizationService orgService) =>
            TelemetryAdapter.GetOrCreate(orgService);

        private static bool ShouldDuplicateInnerTrace(IOrganizationService orgService)
        {
            // Dataverse Environment Variable schema: shi_DisableInnerTraceDuplication.
            // Cached for DisableTraceFlagCacheTtl so a high-volume plug-in doesn't query Dataverse on every
            // Execute. Admins flipping the flag pick it up after the TTL expires.
            var disableFlag =
                Telemetry.EnvironmentVariableReader.GetValue(
                    orgService,
                    "shi_DisableInnerTraceDuplication",
                    DisableTraceFlagCacheTtl
                )
                ?? Environment.GetEnvironmentVariable("shi_DISABLE_INNER_TRACE_DUPLICATION")
                ?? Environment.GetEnvironmentVariable("DISABLE_INNER_TRACE_DUPLICATION");

            return !string.Equals(disableFlag, "1", StringComparison.OrdinalIgnoreCase);
        }
    }
}
