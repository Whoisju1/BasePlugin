using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xrm.Sdk;
using Moq;
using SHI.CRM.Plugins.Base.Telemetry;
using Xunit;

namespace BasePluginTests.Telemetry
{
    public class TelemetryTracingServiceTests
    {
        [Fact]
        public void Trace_invokes_inner_when_duplication_enabled()
        {
            var inner = new Mock<ITracingService>();
            var telemetry = TelemetryAdapter.Create(" "); // disabled adapter; we only care about inner invocation
            var svc = new TelemetryTracingService(
                inner.Object,
                telemetry,
                null,
                duplicateInnerTrace: true
            );

            svc.Trace("hello {0}", 123);

            inner.Verify(t => t.Trace("hello {0}", 123), Times.Once);
        }

        [Fact]
        public void Trace_skips_inner_when_duplication_disabled()
        {
            var inner = new Mock<ITracingService>();
            var telemetry = CreateEnabledAdapter();
            var svc = new TelemetryTracingService(
                inner.Object,
                telemetry,
                null,
                duplicateInnerTrace: false
            );

            svc.Trace("hello {0}", 123);

            inner.Verify(t => t.Trace(It.IsAny<string>(), It.IsAny<object[]>()), Times.Never);
        }

        [Fact]
        public void Trace_invokes_inner_when_duplication_disabled_and_telemetry_disabled()
        {
            var inner = new Mock<ITracingService>();
            var telemetry = TelemetryAdapter.Create(" ");
            var svc = new TelemetryTracingService(
                inner.Object,
                telemetry,
                null,
                duplicateInnerTrace: false
            );

            svc.Trace("hello {0}", 123);

            inner.Verify(t => t.Trace("hello {0}", 123), Times.Once);
        }

        [Fact]
        public void Trace_handles_null_format_gracefully()
        {
            var inner = new Mock<ITracingService>();
            var telemetry = CreateEnabledAdapter();
            var svc = new TelemetryTracingService(
                inner.Object,
                telemetry,
                null,
                duplicateInnerTrace: false
            );

            svc.Trace(null);

            inner.Verify(t => t.Trace(It.IsAny<string>(), It.IsAny<object[]>()), Times.Never);
        }

        private static TelemetryAdapter CreateEnabledAdapter()
        {
            var client = new TestTelemetryClient();
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

            return (TelemetryAdapter)
                ctor.Invoke(
                    new object[]
                    {
                        client,
                        null,
                        typeof(TestTelemetryClient).GetMethod(
                            nameof(TestTelemetryClient.TrackTrace)
                        ),
                        null,
                        null,
                    }
                );
        }

        private sealed class TestTelemetryClient
        {
            public void TrackTrace(string message, IDictionary<string, string> props) { }
        }
    }
}
