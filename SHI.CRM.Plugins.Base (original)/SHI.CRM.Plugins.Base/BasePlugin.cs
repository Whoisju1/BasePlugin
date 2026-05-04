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
    /// Uses <see cref="TelemetryTracingService"/> to mirror traces to Application Insights when available.
    /// Inner tracing duplication is controlled by the DISABLE_INNER_TRACE_DUPLICATION environment variable.
    /// </summary>
    public abstract class BasePlugin : IPlugin
    {
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
            var telemetry = TelemetryAdapter.GetOrCreate(services.OrganizationService);
            if (!telemetry.IsEnabled)
            {
                TelemetryAdapter.TraceTelemetryDisabledOnce(services.Tracing);
            }
            var commonProps = TelemetryAdapter.BuildCommonProperties(services.Context, pluginType);

            var inputCount = services.Context?.InputParameters?.Count ?? 0;
            var sharedCount = services.Context?.SharedVariables?.Count ?? 0;

            // Default to duplicating traces unless explicitly disabled via env flag.
            var duplicateInnerTrace = ShouldDuplicateInnerTrace(services.OrganizationService);

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
                ExecutePluginLogic(
                    serviceProvider,
                    services.Context,
                    services.OrganizationService,
                    services.Tracing,
                    cloudTracing
                );
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
                throw;
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

        protected abstract void ExecutePluginLogic(
            IServiceProvider serviceProvider,
            IPluginExecutionContext context,
            IOrganizationService orgService,
            ITracingService tracing,
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

        private static bool ShouldDuplicateInnerTrace(IOrganizationService orgService)
        {
            // Dataverse Environment Variable schema: shi_DisableInnerTraceDuplication
            var disableFlag =
                Telemetry.EnvironmentVariableReader.GetValue(
                    orgService,
                    "shi_DisableInnerTraceDuplication"
                )
                ?? Environment.GetEnvironmentVariable("shi_DISABLE_INNER_TRACE_DUPLICATION")
                ?? Environment.GetEnvironmentVariable("DISABLE_INNER_TRACE_DUPLICATION");

            return !string.Equals(disableFlag, "1", StringComparison.OrdinalIgnoreCase);
        }
    }
}
