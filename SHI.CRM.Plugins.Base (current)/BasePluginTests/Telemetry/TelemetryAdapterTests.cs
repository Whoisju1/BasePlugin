using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
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
            adapter.TrackException(
                new InvalidOperationException(),
                new Dictionary<string, string>()
            );
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

        [Fact]
        public void ResolveConnectionString_returns_dataverse_explicit_value_when_present()
        {
            var definition = new Entity("environmentvariabledefinition")
            {
                ["defaultvalue"] = "default-conn",
                ["v.value"] = new AliasedValue(
                    "environmentvariablevalue",
                    "value",
                    "explicit-conn"
                ),
            };

            var organizationService = new Mock<IOrganizationService>();
            organizationService
                .Setup(service => service.RetrieveMultiple(It.IsAny<QueryExpression>()))
                .Returns(new EntityCollection(new[] { definition }));

            var value = TelemetryAdapter.ResolveConnectionString(organizationService.Object);

            Assert.Equal("explicit-conn", value);
        }

        [Fact]
        public void ResolveConnectionString_returns_dataverse_default_when_no_explicit_value()
        {
            var organizationService = new Mock<IOrganizationService>();
            organizationService
                .Setup(service => service.RetrieveMultiple(It.IsAny<QueryExpression>()))
                .Returns(
                    new EntityCollection
                    {
                        Entities =
                        {
                            new Entity("environmentvariabledefinition")
                            {
                                ["defaultvalue"] = "default-conn",
                            },
                        },
                    }
                );

            var value = TelemetryAdapter.ResolveConnectionString(organizationService.Object);

            Assert.Equal("default-conn", value);
        }

        [Fact]
        public void ResolveConnectionString_returns_null_when_org_service_is_null_and_env_is_blank()
        {
            var originalApplicationInsights = Environment.GetEnvironmentVariable(
                "APPLICATIONINSIGHTS_CONNECTION_STRING"
            );
            var originalAppInsights = Environment.GetEnvironmentVariable(
                "APPINSIGHTS_CONNECTION_STRING"
            );

            try
            {
                Environment.SetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING", null);
                Environment.SetEnvironmentVariable("APPINSIGHTS_CONNECTION_STRING", null);

                var value = TelemetryAdapter.ResolveConnectionString(null);

                Assert.Null(value);
            }
            finally
            {
                Environment.SetEnvironmentVariable(
                    "APPLICATIONINSIGHTS_CONNECTION_STRING",
                    originalApplicationInsights
                );
                Environment.SetEnvironmentVariable(
                    "APPINSIGHTS_CONNECTION_STRING",
                    originalAppInsights
                );
            }
        }

        [Fact]
        public void ResolveConnectionString_queries_for_expected_schema_name()
        {
            QueryExpression captured = null;
            var organizationService = new Mock<IOrganizationService>();
            organizationService
                .Setup(service => service.RetrieveMultiple(It.IsAny<QueryExpression>()))
                .Callback<QueryBase>(q => captured = q as QueryExpression)
                .Returns(new EntityCollection());

            TelemetryAdapter.ResolveConnectionString(organizationService.Object);

            Assert.NotNull(captured);
            Assert.Equal("environmentvariabledefinition", captured.EntityName);
            var condition = Assert.Single(captured.Criteria.Conditions);
            Assert.Equal("schemaname", condition.AttributeName);
            Assert.Equal("shi_ApplicationInsightsConnectionString", condition.Values[0]);
        }

        [Fact]
        public void ResolveConnectionString_falls_back_to_host_environment_when_dataverse_throws()
        {
            var organizationService = new Mock<IOrganizationService>();
            organizationService
                .Setup(service => service.RetrieveMultiple(It.IsAny<QueryExpression>()))
                .Throws(new InvalidOperationException("dataverse offline"));

            var originalApplicationInsights = Environment.GetEnvironmentVariable(
                "APPLICATIONINSIGHTS_CONNECTION_STRING"
            );
            var originalAppInsights = Environment.GetEnvironmentVariable(
                "APPINSIGHTS_CONNECTION_STRING"
            );

            try
            {
                Environment.SetEnvironmentVariable(
                    "APPLICATIONINSIGHTS_CONNECTION_STRING",
                    "host-conn"
                );
                Environment.SetEnvironmentVariable("APPINSIGHTS_CONNECTION_STRING", null);

                var value = TelemetryAdapter.ResolveConnectionString(organizationService.Object);

                Assert.Equal("host-conn", value);
            }
            finally
            {
                Environment.SetEnvironmentVariable(
                    "APPLICATIONINSIGHTS_CONNECTION_STRING",
                    originalApplicationInsights
                );
                Environment.SetEnvironmentVariable(
                    "APPINSIGHTS_CONNECTION_STRING",
                    originalAppInsights
                );
            }
        }

        [Fact]
        public void GetOrCreate_returns_disabled_adapter_when_connection_string_is_missing()
        {
            var originalInstance = GetInstance();
            var originalConnectionString = GetInstanceConnectionString();
            OverrideAdapter(CreateEnabledAdapter(), "old-conn");

            var organizationService = new Mock<IOrganizationService>();
            organizationService
                .Setup(service => service.RetrieveMultiple(It.IsAny<QueryExpression>()))
                .Returns(new EntityCollection());

            try
            {
                var adapter = TelemetryAdapter.GetOrCreate(organizationService.Object);

                Assert.False(adapter.IsEnabled);
            }
            finally
            {
                RestoreAdapter(originalInstance, originalConnectionString);
            }
        }

        [Fact]
        public void GetOrCreate_reuses_enabled_adapter_when_connection_string_matches()
        {
            var originalInstance = GetInstance();
            var originalConnectionString = GetInstanceConnectionString();
            var existing = CreateEnabledAdapter();
            OverrideAdapter(existing, "same-conn");

            var organizationService = new Mock<IOrganizationService>();
            organizationService
                .Setup(service => service.RetrieveMultiple(It.IsAny<QueryExpression>()))
                .Returns(
                    new EntityCollection(
                        new[]
                        {
                            new Entity("environmentvariabledefinition")
                            {
                                ["v.value"] = new AliasedValue(
                                    "environmentvariablevalue",
                                    "value",
                                    "same-conn"
                                ),
                            },
                        }
                    )
                );

            try
            {
                var adapter = TelemetryAdapter.GetOrCreate(organizationService.Object);

                Assert.Same(existing, adapter);
            }
            finally
            {
                RestoreAdapter(originalInstance, originalConnectionString);
            }
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
                        typeof(TestTelemetryClient).GetMethod(
                            nameof(TestTelemetryClient.TrackException)
                        ),
                        typeof(TestTelemetryClient).GetMethod(
                            nameof(TestTelemetryClient.TrackTrace)
                        ),
                        typeof(TestTelemetryClient).GetMethod(
                            nameof(TestTelemetryClient.TrackMetric)
                        ),
                        typeof(TestTelemetryClient).GetMethod(nameof(TestTelemetryClient.Flush)),
                    }
                );
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

        private static void OverrideAdapter(TelemetryAdapter adapter, string connectionString)
        {
            var instance = typeof(TelemetryAdapter).GetField(
                "_instance",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            var instanceConnectionString = typeof(TelemetryAdapter).GetField(
                "_instanceConnectionString",
                BindingFlags.Static | BindingFlags.NonPublic
            );

            instance?.SetValue(null, adapter);
            instanceConnectionString?.SetValue(null, connectionString);
        }

        private static void RestoreAdapter(object instance, string connectionString)
        {
            var instanceField = typeof(TelemetryAdapter).GetField(
                "_instance",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            var instanceConnectionString = typeof(TelemetryAdapter).GetField(
                "_instanceConnectionString",
                BindingFlags.Static | BindingFlags.NonPublic
            );

            instanceField?.SetValue(null, instance);
            instanceConnectionString?.SetValue(null, connectionString);
        }

        private sealed class TestTelemetryClient
        {
            public void TrackMetric(
                string name,
                double value,
                IDictionary<string, string> props
            ) { }

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
