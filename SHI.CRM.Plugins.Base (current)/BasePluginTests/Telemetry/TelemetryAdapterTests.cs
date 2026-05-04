using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xrm.Sdk;
using Moq;
using SHI.CRM.Plugins.Base.Telemetry;
using Xunit;

namespace BasePluginTests.Telemetry
{
    public class TelemetryAdapterTests
    {
        [Fact]
        public void BuildCommonProperties_includes_context_metadata()
        {
            var context = new Mock<IPluginExecutionContext>();
            var correlation = Guid.NewGuid();
            context.Setup(c => c.CorrelationId).Returns(correlation);
            context.Setup(c => c.OperationId).Returns(Guid.Empty); // should be filtered out
            context.Setup(c => c.RequestId).Returns(Guid.NewGuid());
            context.Setup(c => c.MessageName).Returns("Create");
            context.Setup(c => c.PrimaryEntityName).Returns("account");
            context.Setup(c => c.PrimaryEntityId).Returns(Guid.Empty); // empty -> filter
            context.Setup(c => c.Stage).Returns(20);
            context.Setup(c => c.Depth).Returns(2);
            context.Setup(c => c.Mode).Returns(0);
            context.Setup(c => c.InitiatingUserId).Returns(Guid.NewGuid());
            context.Setup(c => c.UserId).Returns(Guid.NewGuid());
            context.Setup(c => c.BusinessUnitId).Returns(Guid.NewGuid());
            context.Setup(c => c.OrganizationId).Returns(Guid.NewGuid());
            context.Setup(c => c.OrganizationName).Returns("Org");
            context.Setup(c => c.SecondaryEntityName).Returns("contact");

            var inputParams = new ParameterCollection { { "Target", new Entity() } };
            context.Setup(c => c.InputParameters).Returns(inputParams);
            var sharedVars = new ParameterCollection { { "Key", "Value" } };
            context.Setup(c => c.SharedVariables).Returns(sharedVars);

            var props = TelemetryAdapter.BuildCommonProperties(context.Object, "My.Plugin");

            Assert.Contains("CorrelationId", props.Keys);
            Assert.Contains("RequestId", props.Keys);
            Assert.Contains("MessageName", props.Keys);
            Assert.Contains("PrimaryEntityName", props.Keys);
            Assert.Contains("Stage", props.Keys);
            Assert.Contains("Depth", props.Keys);
            Assert.Contains("Mode", props.Keys);
            Assert.Contains("InitiatingUserId", props.Keys);
            Assert.Contains("UserId", props.Keys);
            Assert.Contains("BusinessUnitId", props.Keys);
            Assert.Contains("OrganizationId", props.Keys);
            Assert.Contains("OrganizationName", props.Keys);
            Assert.Contains("SecondaryEntityName", props.Keys);
            Assert.Contains("InputParameterCount", props.Keys);
            Assert.Contains("SharedVariableCount", props.Keys);
            Assert.DoesNotContain("OperationId", props.Keys); // filtered empty
            Assert.DoesNotContain("PrimaryEntityId", props.Keys); // filtered empty
            Assert.Equal("My.Plugin", props["PluginType"]);
        }

        [Fact]
        public void CloneWith_adds_value_when_not_empty_and_keeps_existing()
        {
            var source = new Dictionary<string, string> { { "CorrelationId", "abc" } };

            var clone = TelemetryAdapter.CloneWith(source, "ErrorCode", "123");

            Assert.Equal("abc", clone["CorrelationId"]);
            Assert.Equal("123", clone["ErrorCode"]);
        }

        [Fact]
        public void CloneWith_ignores_empty_key_or_value()
        {
            var source = new Dictionary<string, string>();

            var clone = TelemetryAdapter.CloneWith(source, "", "");

            Assert.Empty(clone);
        }

        [Fact]
        public void Disabled_adapter_noops_and_does_not_throw()
        {
            var adapter = TelemetryAdapter.Create(" "); // force disabled

            adapter.TrackTrace("msg", new Dictionary<string, string>());
            adapter.TrackException(new InvalidOperationException(), new Dictionary<string, string>());
            adapter.Flush();

            Assert.False(adapter.IsEnabled);
        }

        [Fact]
        public void TraceTelemetryDisabledOnce_traces_only_once()
        {
            var tracing = new Mock<ITracingService>();

            var flag = typeof(TelemetryAdapter).GetField(
                "_disableNoticeTraced",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            flag?.SetValue(null, false);

            TelemetryAdapter.TraceTelemetryDisabledOnce(tracing.Object);
            TelemetryAdapter.TraceTelemetryDisabledOnce(tracing.Object);

            tracing.Verify(t => t.Trace(It.IsAny<string>()), Times.Once);
        }
    }
}
