using System;
using System.Collections.Generic;
using System.Reflection;
using BasePluginTests.Common;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using SHI.CRM.Plugins.Base.Telemetry;
using Xunit;

namespace BasePluginTests
{
    public class BasePluginMetricsTests : IDisposable
    {
        private readonly PluginTestHarness _harness = new();
        private readonly object _originalInstance;
        private readonly string _originalConnectionString;

        public BasePluginMetricsTests()
        {
            _originalInstance = GetInstance();
            _originalConnectionString = GetInstanceConnectionString();
        }

        [Fact]
        public void Does_not_emit_metrics_when_disabled()
        {
            // Arrange: disabled adapter (null client); ensure singleton reset
            OverrideAdapter(null, null, null, null, null);
            _harness.ResetContextParameters();
            var plugin = new TestableChildPlugin();

            // Act/Assert: should not throw even when metrics are skipped
            plugin.Execute(_harness.ServiceProvider.Object);
        }

        [Fact]
        public void Emits_metrics_for_counts_and_duration_when_enabled()
        {
            var spyClient = new TestTelemetryClient();
            SetupDataverseConnectionString("metrics-conn");
            OverrideAdapter(
                spyClient,
                typeof(TestTelemetryClient).GetMethod(nameof(TestTelemetryClient.TrackException)),
                typeof(TestTelemetryClient).GetMethod(nameof(TestTelemetryClient.TrackTrace)),
                typeof(TestTelemetryClient).GetMethod(nameof(TestTelemetryClient.TrackMetric)),
                typeof(TestTelemetryClient).GetMethod(nameof(TestTelemetryClient.Flush)),
                "metrics-conn"
            );

            // set context counts
            var inputParams = new ParameterCollection { { "Target", new Entity("account") } };
            _harness.Context.Setup(c => c.InputParameters).Returns(inputParams);
            var sharedVars = new ParameterCollection { { "Key", "Value" } };
            _harness.Context.Setup(c => c.SharedVariables).Returns(sharedVars);

            Assert.Equal(
                "metrics-conn",
                TelemetryAdapter.ResolveConnectionString(_harness.ExecutionService.Object)
            );
            Assert.True(TelemetryAdapter.GetOrCreate(_harness.ExecutionService.Object).IsEnabled);

            var plugin = new TestableChildPlugin();

            // Act
            plugin.Execute(_harness.ServiceProvider.Object);

            // Assert: metrics emitted
            Assert.Contains("InputParameterCount", spyClient.MetricNames);
            Assert.Contains("SharedVariableCount", spyClient.MetricNames);
            Assert.Contains("TotalDurationMs", spyClient.MetricNames);
        }

        public void Dispose()
        {
            RestoreInstance(_originalInstance, _originalConnectionString);
        }

        private static object GetInstance()
        {
            var field = typeof(TelemetryAdapter).GetField(
                "_instance",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            return field?.GetValue(null);
        }

        private static string GetInstanceConnectionString()
        {
            var field = typeof(TelemetryAdapter).GetField(
                "_instanceConnectionString",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            return field?.GetValue(null) as string;
        }

        private static void RestoreInstance(object instance, string connectionString)
        {
            var field = typeof(TelemetryAdapter).GetField(
                "_instance",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            var connectionStringField = typeof(TelemetryAdapter).GetField(
                "_instanceConnectionString",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            field?.SetValue(null, instance);
            connectionStringField?.SetValue(null, connectionString);
        }

        private static void OverrideAdapter(
            object client,
            MethodInfo trackException,
            MethodInfo trackTrace,
            MethodInfo trackMetric,
            MethodInfo flush,
            string connectionString = null
        )
        {
            var ctor = typeof(TelemetryAdapter).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                new[]
                {
                    typeof(object),
                    typeof(MethodInfo),
                    typeof(MethodInfo),
                    typeof(MethodInfo),
                    typeof(MethodInfo),
                },
                modifiers: null
            );

            var adapter = ctor?.Invoke(
                new object[] { client, trackException, trackTrace, trackMetric, flush }
            );
            var field = typeof(TelemetryAdapter).GetField(
                "_instance",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            var connectionStringField = typeof(TelemetryAdapter).GetField(
                "_instanceConnectionString",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            field?.SetValue(null, adapter);
            connectionStringField?.SetValue(null, connectionString);
        }

        private void SetupDataverseConnectionString(string connectionString)
        {
            _harness
                .ExecutionService.Setup(s => s.RetrieveMultiple(It.IsAny<QueryExpression>()))
                .Returns<QueryBase>(query =>
                {
                    var qe = query as QueryExpression;
                    var schema = qe?.Criteria.Conditions[0].Values[0] as string;
                    if (schema == "shi_ApplicationInsightsConnectionString")
                    {
                        var entity = new Entity("environmentvariabledefinition")
                        {
                            ["v.value"] = new AliasedValue(
                                "environmentvariablevalue",
                                "value",
                                connectionString
                            ),
                        };
                        return new EntityCollection(new[] { entity });
                    }

                    return new EntityCollection();
                });
        }

        private class TestTelemetryClient
        {
            public List<string> MetricNames { get; } = new();

            public void TrackMetric(string name, double value, IDictionary<string, string> props)
            {
                MetricNames.Add(name ?? string.Empty);
            }

            public void TrackTrace(string message, IDictionary<string, string> props) { }

            public void TrackException(
                Exception ex,
                IDictionary<string, string> props,
                IDictionary<string, double> metrics
            ) { }

            public void Flush() { }
        }
    }
}
