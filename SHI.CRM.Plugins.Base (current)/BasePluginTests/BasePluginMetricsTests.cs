using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xrm.Sdk;
using Moq;
using SHI.CRM.Plugins.Base.Telemetry;
using Xunit;
using BasePluginTests.Common;

namespace BasePluginTests
{
    public class BasePluginMetricsTests : IDisposable
    {
        private readonly PluginTestHarness _harness = new();
        private readonly object _originalInstance;

        public BasePluginMetricsTests()
        {
            _originalInstance = GetInstance();
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
            OverrideAdapter(
                spyClient,
                typeof(TestTelemetryClient).GetMethod(nameof(TestTelemetryClient.TrackException)),
                typeof(TestTelemetryClient).GetMethod(nameof(TestTelemetryClient.TrackTrace)),
                typeof(TestTelemetryClient).GetMethod(nameof(TestTelemetryClient.TrackMetric)),
                typeof(TestTelemetryClient).GetMethod(nameof(TestTelemetryClient.Flush))
            );

            // set context counts
            var inputParams = new ParameterCollection { { "Target", new Entity("account") } };
            _harness.Context.Setup(c => c.InputParameters).Returns(inputParams);
            var sharedVars = new ParameterCollection { { "Key", "Value" } };
            _harness.Context.Setup(c => c.SharedVariables).Returns(sharedVars);

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
            RestoreInstance(_originalInstance);
        }

        private static object GetInstance()
        {
            var field = typeof(TelemetryAdapter).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
            return field?.GetValue(null);
        }

        private static void RestoreInstance(object instance)
        {
            var field = typeof(TelemetryAdapter).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
            field?.SetValue(null, instance);
        }

        private static void OverrideAdapter(
            object client,
            MethodInfo trackException,
            MethodInfo trackTrace,
            MethodInfo trackMetric,
            MethodInfo flush)
        {
            var ctor = typeof(TelemetryAdapter).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(object), typeof(MethodInfo), typeof(MethodInfo), typeof(MethodInfo), typeof(MethodInfo) },
                modifiers: null
            );

            var adapter = ctor?.Invoke(new object[] { client, trackException, trackTrace, trackMetric, flush });
            var field = typeof(TelemetryAdapter).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
            field?.SetValue(null, adapter);
        }

        private class TestTelemetryClient
        {
            public List<string> MetricNames { get; } = new();

            public void TrackMetric(string name, double value, IDictionary<string, string> props)
            {
                MetricNames.Add(name ?? string.Empty);
            }

            public void TrackTrace(string message, IDictionary<string, string> props) { }

            public void TrackException(Exception ex, IDictionary<string, string> props, IDictionary<string, double> metrics) { }

            public void Flush() { }
        }
    }
}