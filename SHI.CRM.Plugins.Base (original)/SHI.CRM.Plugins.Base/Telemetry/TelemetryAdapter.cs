using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace SHI.CRM.Plugins.Base.Telemetry
{
    /// <summary>
    /// Lightweight Application Insights bridge that no-ops when the AI SDK or connection string is unavailable.
    /// </summary>
    internal sealed class TelemetryAdapter
    {
        private static readonly object _lock = new object();
        private static TelemetryAdapter _instance;
        private static bool _disableNoticeTraced;

        private readonly object _client;
        private readonly MethodInfo _trackException;
        private readonly MethodInfo _trackTrace;
        private readonly MethodInfo _trackMetric;
        private readonly MethodInfo _flush;

        public bool IsEnabled => _client != null;

        private TelemetryAdapter(
            object client,
            MethodInfo trackException,
            MethodInfo trackTrace,
            MethodInfo trackMetric,
            MethodInfo flush
        )
        {
            _client = client;
            _trackException = trackException;
            _trackTrace = trackTrace;
            _trackMetric = trackMetric;
            _flush = flush;
        }

        /// <summary>
        /// Creates an adapter using a Dataverse Environment Variable (schema: shi_ApplicationInsightsConnectionString),
        /// falling back to host environment variables.
        /// </summary>
        public static TelemetryAdapter CreateFromDataverseOrEnvironment(
            IOrganizationService orgService
        )
        {
            var connectionString =
                TryGetConnectionStringFromDataverse(orgService)
                ?? Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")
                ?? Environment.GetEnvironmentVariable("APPINSIGHTS_CONNECTION_STRING");

            return Create(connectionString);
        }

        /// <summary>
        /// Returns a singleton adapter created from environment configuration. Subsequent calls reuse the instance.
        /// </summary>
        public static TelemetryAdapter GetOrCreate(IOrganizationService orgService = null)
        {
            if (_instance != null)
                return _instance;

            lock (_lock)
            {
                if (_instance == null)
                {
                    _instance = CreateFromDataverseOrEnvironment(orgService);
                }
            }

            return _instance;
        }

        /// <summary>
        /// Creates an adapter using the provided connection string. Returns a disabled adapter on failure.
        /// </summary>
        public static TelemetryAdapter Create(string connectionString)
        {
            try
            {
                var clientType = Type.GetType(
                    "Microsoft.ApplicationInsights.TelemetryClient, Microsoft.ApplicationInsights"
                );
                if (clientType == null)
                    return new TelemetryAdapter(null, null, null, null, null);

                object clientInstance = Activator.CreateInstance(clientType);

                if (!string.IsNullOrWhiteSpace(connectionString))
                {
                    var configType = Type.GetType(
                        "Microsoft.ApplicationInsights.Extensibility.TelemetryConfiguration, Microsoft.ApplicationInsights"
                    );
                    var activeProp = configType?.GetProperty(
                        "Active",
                        BindingFlags.Static | BindingFlags.Public
                    );
                    var activeConfig = activeProp?.GetValue(null);
                    var csProp = configType?.GetProperty(
                        "ConnectionString",
                        BindingFlags.Public | BindingFlags.Instance
                    );
                    csProp?.SetValue(activeConfig, connectionString);
                }

                var trackException = clientType.GetMethod(
                    "TrackException",
                    new[]
                    {
                        typeof(Exception),
                        typeof(IDictionary<string, string>),
                        typeof(IDictionary<string, double>),
                    }
                );

                var trackTrace =
                    clientType.GetMethod(
                        "TrackTrace",
                        new[] { typeof(string), typeof(IDictionary<string, string>) }
                    ) ?? clientType.GetMethod("TrackTrace", new[] { typeof(string) });

                var trackMetric =
                    clientType.GetMethod(
                        "TrackMetric",
                        new[]
                        {
                            typeof(string),
                            typeof(double),
                            typeof(IDictionary<string, string>),
                        }
                    )
                    ?? clientType.GetMethod(
                        "TrackMetric",
                        new[] { typeof(string), typeof(double) }
                    );

                var flush = clientType.GetMethod(
                    "Flush",
                    BindingFlags.Public | BindingFlags.Instance
                );

                return new TelemetryAdapter(
                    clientInstance,
                    trackException,
                    trackTrace,
                    trackMetric,
                    flush
                );
            }
            catch
            {
                return new TelemetryAdapter(null, null, null, null, null);
            }
        }

        /// <summary>
        /// Sends an exception to Application Insights when enabled. Swallows telemetry errors.
        /// </summary>
        public void TrackException(Exception ex, IDictionary<string, string> properties)
        {
            if (_client == null || _trackException == null || ex == null)
                return;

            try
            {
                _trackException.Invoke(_client, new object[] { ex, properties, null });
            }
            catch
            {
                // swallow telemetry errors to avoid impacting plugin execution
            }
        }

        /// <summary>
        /// Sends a trace message to Application Insights when enabled. Swallows telemetry errors.
        /// </summary>
        public void TrackTrace(string message, IDictionary<string, string> properties)
        {
            if (_client == null || _trackTrace == null || string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                if (_trackTrace.GetParameters().Length == 2)
                {
                    _trackTrace.Invoke(_client, new object[] { message, properties });
                }
                else
                {
                    _trackTrace.Invoke(_client, new object[] { message });
                }
            }
            catch
            {
                // swallow telemetry errors to avoid impacting plugin execution
            }
        }

        /// <summary>
        /// Sends a metric to Application Insights when enabled. Swallows telemetry errors.
        /// </summary>
        public void TrackMetric(string name, double value, IDictionary<string, string> properties)
        {
            if (_client == null || _trackMetric == null || string.IsNullOrWhiteSpace(name))
                return;

            try
            {
                if (_trackMetric.GetParameters().Length == 3)
                {
                    _trackMetric.Invoke(_client, new object[] { name, value, properties });
                }
                else
                {
                    _trackMetric.Invoke(_client, new object[] { name, value });
                }
            }
            catch
            {
                // swallow telemetry errors to avoid impacting plugin execution
            }
        }

        /// <summary>
        /// Flushes buffered telemetry when enabled. Swallows telemetry errors.
        /// </summary>
        public void Flush()
        {
            if (_client == null || _flush == null)
                return;

            try
            {
                _flush.Invoke(_client, null);
            }
            catch
            {
                // swallow telemetry errors to avoid impacting plugin execution
            }
        }

        /// <summary>
        /// Builds common telemetry properties from the plugin execution context. Empty values are omitted.
        /// </summary>
        /// <param name="context">Execution context used for correlation identifiers.</param>
        /// <param name="pluginType">Fully-qualified plugin type name to attribute events.</param>
        public static Dictionary<string, string> BuildCommonProperties(
            IPluginExecutionContext context,
            string pluginType = null
        )
        {
            var props = new Dictionary<string, string>();

            AddIfNotEmpty(props, "CorrelationId", context?.CorrelationId);
            AddIfNotEmpty(props, "OperationId", context?.OperationId);
            AddIfNotEmpty(props, "RequestId", context?.RequestId);
            AddIfNotEmpty(props, "MessageName", context?.MessageName);
            AddIfNotEmpty(props, "PrimaryEntityName", context?.PrimaryEntityName);
            AddIfNotEmpty(props, "PrimaryEntityId", context?.PrimaryEntityId);
            AddIfNotEmpty(props, "SecondaryEntityName", context?.SecondaryEntityName);
            AddIfNotEmpty(props, "Stage", context?.Stage.ToString());
            AddIfNotEmpty(props, "Depth", context?.Depth.ToString());
            AddIfNotEmpty(props, "Mode", context?.Mode.ToString());
            AddIfNotEmpty(props, "InitiatingUserId", context?.InitiatingUserId);
            AddIfNotEmpty(props, "UserId", context?.UserId);
            AddIfNotEmpty(props, "BusinessUnitId", context?.BusinessUnitId);
            AddIfNotEmpty(props, "OrganizationId", context?.OrganizationId);
            AddIfNotEmpty(props, "OrganizationName", context?.OrganizationName);
            AddIfPositive(props, "InputParameterCount", context?.InputParameters?.Count);
            AddIfPositive(props, "SharedVariableCount", context?.SharedVariables?.Count);
            AddIfNotEmpty(props, "PluginType", pluginType);

            return props;
        }

        /// <summary>
        /// Returns a shallow clone of the source dictionary with an optional key/value added when both are non-empty.
        /// </summary>
        public static Dictionary<string, string> CloneWith(
            IDictionary<string, string> source,
            string key,
            string value
        )
        {
            var clone = new Dictionary<string, string>(source ?? new Dictionary<string, string>());
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                clone[key] = value;
            }
            return clone;
        }

        /// <summary>
        /// Writes a single trace line indicating telemetry is disabled. Guarded to avoid repeated noise.
        /// </summary>
        public static void TraceTelemetryDisabledOnce(ITracingService tracing)
        {
            if (_disableNoticeTraced)
                return;

            lock (_lock)
            {
                if (_disableNoticeTraced)
                    return;

                _disableNoticeTraced = true;
                tracing?.Trace(
                    "Telemetry disabled: missing Application Insights SDK or connection string."
                );
            }
        }

        private static string TryGetConnectionStringFromDataverse(IOrganizationService orgService)
        {
            if (orgService == null)
                return null;

            try
            {
                var query = new QueryExpression("environmentvariabledefinition")
                {
                    ColumnSet = new ColumnSet("defaultvalue"),
                    Criteria =
                    {
                        Conditions =
                        {
                            new ConditionExpression(
                                "schemaname",
                                ConditionOperator.Equal,
                                "shi_ApplicationInsightsConnectionString"
                            ),
                        },
                    },
                };

                query.LinkEntities.Add(
                    new LinkEntity(
                        "environmentvariabledefinition",
                        "environmentvariablevalue",
                        "environmentvariabledefinitionid",
                        "environmentvariabledefinitionid",
                        JoinOperator.LeftOuter
                    )
                    {
                        Columns = new ColumnSet("value"),
                        EntityAlias = "v",
                    }
                );

                var result = orgService.RetrieveMultiple(query);
                if (result?.Entities == null || result.Entities.Count == 0)
                    return null;

                var definition = result.Entities[0];
                var explicitValue =
                    definition.GetAttributeValue<AliasedValue>("v.value")?.Value as string;
                if (!string.IsNullOrWhiteSpace(explicitValue))
                    return explicitValue;

                var defaultValue = definition.GetAttributeValue<string>("defaultvalue");
                return string.IsNullOrWhiteSpace(defaultValue) ? null : defaultValue;
            }
            catch
            {
                return null;
            }
        }

        private static void AddIfNotEmpty(
            IDictionary<string, string> target,
            string key,
            string value
        )
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                return;

            target[key] = value;
        }

        private static void AddIfNotEmpty(
            IDictionary<string, string> target,
            string key,
            Guid? value
        )
        {
            if (!value.HasValue || value.Value == Guid.Empty)
                return;

            AddIfNotEmpty(target, key, value.Value.ToString());
        }

        private static void AddIfPositive(
            IDictionary<string, string> target,
            string key,
            int? value
        )
        {
            if (!value.HasValue || value.Value <= 0)
                return;

            AddIfNotEmpty(target, key, value.Value.ToString());
        }
    }
}
