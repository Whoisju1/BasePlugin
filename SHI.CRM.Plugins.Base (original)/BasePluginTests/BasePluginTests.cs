using System;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Moq;
using Xunit;
using BasePluginTests.Common;

namespace BasePluginTests
{
    public class BasePluginTests
    {
        private readonly PluginTestHarness _harness = new();

        public BasePluginTests()
        {
        }

        [Fact]
        public void Execute_throws_when_service_provider_is_null()
        {
            var plugin = new TestableChildPlugin();

            Assert.Throws<ArgumentNullException>(() => plugin.Execute(null));
        }

        [Fact]
        public void Execute_throws_and_traces_when_context_missing()
        {
            _harness.ServiceProvider
                .Setup(sp => sp.GetService(typeof(IPluginExecutionContext)))
                .Returns((IPluginExecutionContext)null);

            var plugin = new TestableChildPlugin();

            var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
                plugin.Execute(_harness.ServiceProvider.Object)
            );

            Assert.Contains("context", ex.Message, StringComparison.OrdinalIgnoreCase);
            _harness.Tracing.Verify(t => t.Trace(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public void Execute_throws_and_traces_when_service_factory_missing()
        {
            _harness.ServiceProvider
                .Setup(sp => sp.GetService(typeof(IOrganizationServiceFactory)))
                .Returns((IOrganizationServiceFactory)null);

            var plugin = new TestableChildPlugin();

            var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
                plugin.Execute(_harness.ServiceProvider.Object)
            );

            Assert.Contains("service factory", ex.Message, StringComparison.OrdinalIgnoreCase);
            _harness.Tracing.Verify(t => t.Trace(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public void Execute_invokes_child_logic_with_org_service_from_factory()
        {
            var userId = Guid.NewGuid();
            _harness.Context.Setup(c => c.UserId).Returns(userId);
            _harness.Factory
                .Setup(f => f.CreateOrganizationService(userId))
                .Returns(_harness.OrganizationService.Object);

            IPluginExecutionContext capturedContext = null;
            IOrganizationService capturedService = null;
            ITracingService capturedCloudTracing = null;
            var plugin = new TestableChildPlugin(
                (sp, ctx, svc, trace, cloudTrace) =>
                {
                    capturedContext = ctx;
                    capturedService = svc;
                    capturedCloudTracing = cloudTrace;
                }
            );

            plugin.Execute(_harness.ServiceProvider.Object);

            Assert.Same(_harness.Context.Object, capturedContext);
            Assert.Same(_harness.OrganizationService.Object, capturedService);
            Assert.NotNull(capturedCloudTracing);
            AssertCommonPropsIncludeExpectedFields(capturedCloudTracing);
            _harness.Factory.Verify(f => f.CreateOrganizationService(userId), Times.Once);
        }

        [Fact]
        public void Execute_traces_and_rethrows_business_exception()
        {
            var plugin = TestableChildPlugin.ThatThrows(
                new InvalidPluginExecutionException("boom")
            );

            var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
                plugin.Execute(_harness.ServiceProvider.Object)
            );

            Assert.Equal("boom", ex.Message);
            _harness.Tracing.Verify(
                t =>
                    t.Trace(
                        It.IsAny<string>(),
                        It.Is<object[]>(o =>
                            o.Length > 0
                            && o[0] != null
                            && o[0].ToString().Contains("Business exception")
                        )
                    ),
                Times.Once
            );
        }

        [Fact]
        public void Execute_traces_and_rethrows_fault_exception()
        {
            var fault = new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { Message = "fault" }
            );
            var plugin = TestableChildPlugin.ThatThrows(fault);

            var ex = Assert.Throws<FaultException<OrganizationServiceFault>>(() =>
                plugin.Execute(_harness.ServiceProvider.Object)
            );

            Assert.Equal("fault", ex.Detail.Message);
            _harness.Tracing.Verify(
                t =>
                    t.Trace(
                        It.IsAny<string>(),
                        It.Is<object[]>(o =>
                            o.Length > 0
                            && o[0] != null
                            && o[0].ToString().Contains("OrganizationService fault")
                        )
                    ),
                Times.Once
            );
        }

        [Fact]
        public void Execute_traces_and_rethrows_unhandled_exception()
        {
            var plugin = TestableChildPlugin.ThatThrows(new InvalidOperationException("oops"));

            var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
                plugin.Execute(_harness.ServiceProvider.Object)
            );
            string errMsg = "Something went wrong. Please try again. If this keeps happening, contact support.";
            Assert.Contains(errMsg, ex.Message);
            Assert.DoesNotContain("Reference:", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.IsType<InvalidOperationException>(ex.InnerException);
            _harness.Tracing.Verify(
                t =>
                    t.Trace(
                        It.IsAny<string>(),
                        It.Is<object[]>(o =>
                            o.Length > 0
                            && o[0] != null
                            && o[0].ToString().Contains("Unhandled plugin exception")
                        )
                    ),
                Times.Once
            );
        }

        [Fact]
        public void Execution_presents_reference_id_on_unhandled_exception()
        {
            var correlation = Guid.NewGuid();
            _harness.Context.Setup(c => c.CorrelationId).Returns(correlation);

            var plugin = TestableChildPlugin.ThatThrows(new InvalidOperationException("oops"));

            var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
                plugin.Execute(_harness.ServiceProvider.Object)
            );

            var prefix = correlation.ToString("N").Substring(0, 8);
            Assert.Contains(prefix, ex.Message);
            Assert.Contains("Reference", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("unknown", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        private void AssertCommonPropsIncludeExpectedFields(ITracingService cloudTracing)
        {
            var telemetryTracer = Assert.IsType<SHI.CRM.Plugins.Base.Telemetry.TelemetryTracingService>(cloudTracing);
            var field = typeof(SHI.CRM.Plugins.Base.Telemetry.TelemetryTracingService)
                .GetField("_commonProps", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            var props = Assert.IsAssignableFrom<System.Collections.Generic.IReadOnlyDictionary<string, string>>(field?.GetValue(telemetryTracer));
            Assert.Equal(typeof(TestableChildPlugin).FullName, props["PluginType"]);
            Assert.True(props.ContainsKey("TraceDuplicationEnabled"));
            Assert.True(props.ContainsKey("TelemetryEnabled"));
            Assert.Equal(_harness.Context.Object.Stage.ToString(), props["Stage"]);
            Assert.Equal(_harness.Context.Object.Depth.ToString(), props["Depth"]);
        }
    }
}
