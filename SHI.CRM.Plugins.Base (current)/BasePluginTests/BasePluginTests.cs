using System;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
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
            var initiatingUserId = Guid.NewGuid();
            _harness.SetDefaultContext(userId, initiatingUserId);
            _harness.Factory
                .Setup(f => f.CreateOrganizationService(userId))
                .Returns(_harness.ExecutionService.Object);
            _harness.Factory
                .Setup(f => f.CreateOrganizationService(initiatingUserId))
                .Returns(_harness.PermissionCheckService.Object);

            SHI.CRM.Plugins.Base.Infrastructure.PluginServices capturedServices = null;
            ITracingService capturedCloudTracing = null;
            var plugin = new TestableChildPlugin(
                (sp, services, cloudTrace) =>
                {
                    capturedServices = services;
                    capturedCloudTracing = cloudTrace;
                }
            );

            plugin.Execute(_harness.ServiceProvider.Object);

            Assert.NotNull(capturedServices);
            Assert.Same(_harness.Context.Object, capturedServices.Context);
            Assert.Same(_harness.Tracing.Object, capturedServices.Tracing);
            Assert.Same(_harness.ExecutionService.Object, capturedServices.ExecutionService);
            Assert.Same(_harness.PermissionCheckService.Object, capturedServices.PermissionCheckService);
            Assert.NotNull(capturedCloudTracing);
            AssertCommonPropsIncludeExpectedFields(capturedCloudTracing);
            _harness.Factory.Verify(f => f.CreateOrganizationService(userId), Times.Once);
            _harness.Factory.Verify(f => f.CreateOrganizationService(initiatingUserId), Times.Once);
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
        public void Execute_traces_and_wraps_fault_exception()
        {
            var fault = new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { Message = "fault" }
            );
            var plugin = TestableChildPlugin.ThatThrows(fault);

            var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
                plugin.Execute(_harness.ServiceProvider.Object)
            );

            Assert.Same(fault, ex.InnerException);
            Assert.Contains("Something went wrong", ex.Message);
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
        public void Execute_marks_async_service_protection_fault_for_retry()
        {
            _harness.Context.Setup(c => c.Mode).Returns(1);
            var fault = new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault
                {
                    ErrorCode = -2147015902,
                    Message = "Service protection limit exceeded.",
                }
            );
            var plugin = TestableChildPlugin.ThatThrows(fault);

            var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
                plugin.Execute(_harness.ServiceProvider.Object)
            );

            Assert.Equal(OperationStatus.Retry, ex.Status);
            Assert.Contains("marked for retry", ex.Message);
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

        [Fact]
        public void Execute_reads_disable_trace_flag_from_dataverse_when_set()
        {
            // Configure the execution service to return "1" for the Dataverse env var so duplication is disabled.
            _harness.ExecutionService
                .Setup(s => s.RetrieveMultiple(It.IsAny<QueryExpression>()))
                .Returns<QueryBase>(query =>
                {
                    var qe = query as QueryExpression;
                    var schema = qe?.Criteria.Conditions[0].Values[0] as string;
                    if (schema == "shi_DisableInnerTraceDuplication")
                    {
                        var entity = new Entity("environmentvariabledefinition")
                        {
                            ["v.value"] = new AliasedValue(
                                "environmentvariablevalue",
                                "value",
                                "1"
                            ),
                        };
                        return new EntityCollection(new[] { entity });
                    }

                    return new EntityCollection();
                });

            ITracingService capturedCloudTracing = null;
            var plugin = new TestableChildPlugin(
                (sp, services, cloudTrace) => capturedCloudTracing = cloudTrace
            );

            plugin.Execute(_harness.ServiceProvider.Object);

            var telemetryTracer = Assert.IsType<SHI.CRM.Plugins.Base.Telemetry.TelemetryTracingService>(capturedCloudTracing);
            var field = typeof(SHI.CRM.Plugins.Base.Telemetry.TelemetryTracingService)
                .GetField("_duplicateInnerTrace", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var duplicateInnerTrace = (bool)field!.GetValue(telemetryTracer)!;
            Assert.False(duplicateInnerTrace);

            // The execution service should have been queried for the Dataverse env var.
            _harness.ExecutionService.Verify(
                s => s.RetrieveMultiple(It.Is<QueryExpression>(q =>
                    q.EntityName == "environmentvariabledefinition"
                    && (string)q.Criteria.Conditions[0].Values[0] == "shi_DisableInnerTraceDuplication"
                )),
                Times.AtLeastOnce
            );
        }

        [Fact]
        public void Execute_defaults_to_duplicating_inner_trace_when_dataverse_has_no_value()
        {
            // No setup on RetrieveMultiple => Moq returns null, EnvironmentVariableReader returns null.
            ITracingService capturedCloudTracing = null;
            var plugin = new TestableChildPlugin(
                (sp, services, cloudTrace) => capturedCloudTracing = cloudTrace
            );

            plugin.Execute(_harness.ServiceProvider.Object);

            var telemetryTracer = Assert.IsType<SHI.CRM.Plugins.Base.Telemetry.TelemetryTracingService>(capturedCloudTracing);
            var field = typeof(SHI.CRM.Plugins.Base.Telemetry.TelemetryTracingService)
                .GetField("_duplicateInnerTrace", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var duplicateInnerTrace = (bool)field!.GetValue(telemetryTracer)!;
            Assert.True(duplicateInnerTrace);
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
