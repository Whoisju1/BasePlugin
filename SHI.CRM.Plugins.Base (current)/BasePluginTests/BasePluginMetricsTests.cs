using System;
using System.Collections.Generic;
using System.Reflection;
using BasePluginTests.Common;
using Microsoft.Xrm.Sdk;
using Moq;
using SHI.CRM.Plugins.Base.Telemetry;
using Xunit;

namespace BasePluginTests
{
    public class BasePluginMetricsTests
    {
        private readonly PluginTestHarness _harness = new();

        public BasePluginMetricsTests()
        {
            // Ensure cached env-var values from earlier tests don't bleed in.
            EnvironmentVariableReader.ClearCache();
        }

        [Fact]
        public void Does_not_emit_metrics_when_disabled()
        {
            _harness.ResetContextParameters();
            var plugin = new TestableChildPlugin
            {
                TelemetryOverride = BuildAdapter(client: null),
            };

            // Act/Assert: should not throw even when metrics are skipped
            plugin.Execute(_harness.ServiceProvider.Object);
        }

        [Fact]
        public void Emits_metrics_for_counts_and_duration_when_enabled()
        {
            var spyClient = new TestTelemetryClient();
            var plugin = new TestableChildPlugin
            {
                TelemetryOverride = BuildAdapter(spyClient),
            };

            // set context counts
            var inputParams = new ParameterCollection { { "Target", new Entity("account") } };
            _harness.Context.Setup(c => c.InputParameters).Returns(inputParams);
            var sharedVars = new ParameterCollection { { "Key", "Value" } };
            _harness.Context.Setup(c => c.SharedVariables).Returns(sharedVars);

            // Act
            plugin.Execute(_harness.ServiceProvider.Object);

            // Assert: metrics emitted
            Assert.Contains("InputParameterCount", spyClient.MetricNames);
            Assert.Contains("SharedVariableCount", spyClient.MetricNames);
            Assert.Contains("TotalDurationMs", spyClient.MetricNames);
        }

        private static TelemetryAdapter BuildAdapter(TestTelemetryClient client)
        {
            // Reach the internal constructor once; simpler than another seam and keeps the helper local to tests.
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

            if (client == null)
            {
                return (TelemetryAdapter)
                    ctor!.Invoke(new object[] { null, null, null, null, null });
            }

            return (TelemetryAdapter)ctor!.Invoke(
                new object[]
                {
                    client,
                    typeof(TestTelemetryClient).GetMethod(nameof(TestTelemetryClient.TrackException)),
                    typeof(TestTelemetryClient).GetMethod(nameof(TestTelemetryClient.TrackTrace)),
                    typeof(TestTelemetryClient).GetMethod(nameof(TestTelemetryClient.TrackMetric)),
                    typeof(TestTelemetryClient).GetMethod(nameof(TestTelemetryClient.Flush)),
                }
            );
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
