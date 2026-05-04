using System;
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
            var svc = new TelemetryTracingService(inner.Object, telemetry, null, duplicateInnerTrace: true);

            svc.Trace("hello {0}", 123);

            inner.Verify(t => t.Trace("hello {0}", 123), Times.Once);
        }

        [Fact]
        public void Trace_skips_inner_when_duplication_disabled()
        {
            var inner = new Mock<ITracingService>();
            var telemetry = TelemetryAdapter.Create(" "); // disabled adapter; we only care about duplication flag
            var svc = new TelemetryTracingService(inner.Object, telemetry, null, duplicateInnerTrace: false);

            svc.Trace("hello {0}", 123);

            inner.Verify(t => t.Trace(It.IsAny<string>(), It.IsAny<object[]>()), Times.Never);
        }

        [Fact]
        public void Trace_handles_null_format_gracefully()
        {
            var inner = new Mock<ITracingService>();
            var telemetry = TelemetryAdapter.Create(" ");
            var svc = new TelemetryTracingService(inner.Object, telemetry, null, duplicateInnerTrace: false);

            // Should not throw even with null format
            svc.Trace(null);
            inner.Verify(t => t.Trace(It.IsAny<string>(), It.IsAny<object[]>()), Times.Never);
        }
    }
}
