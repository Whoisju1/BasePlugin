using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using SHI.CRM.Plugins.Base.Telemetry;
using Xunit;

namespace BasePluginTests.Telemetry
{
    public class EnvironmentVariableReaderTests
    {
        public EnvironmentVariableReaderTests()
        {
            EnvironmentVariableReader.ClearCache();
        }

        [Fact]
        public void GetValue_returns_explicit_value_when_present()
        {
            var definition = new Entity("environmentvariabledefinition")
            {
                ["defaultvalue"] = "default-value",
                ["v.value"] = new AliasedValue(
                    "environmentvariablevalue",
                    "value",
                    "explicit-value"
                ),
            };

            var organizationService = new Mock<IOrganizationService>();
            organizationService
                .Setup(service => service.RetrieveMultiple(It.IsAny<QueryExpression>()))
                .Returns(new EntityCollection(new[] { definition }));

            var value = EnvironmentVariableReader.GetValue(
                organizationService.Object,
                "shi_SomeFlag"
            );

            Assert.Equal("explicit-value", value);
        }

        [Fact]
        public void GetValue_returns_default_when_no_explicit_value()
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
                                ["defaultvalue"] = "fallback-value",
                            },
                        },
                    }
                );

            var value = EnvironmentVariableReader.GetValue(
                organizationService.Object,
                "shi_SomeFlag"
            );

            Assert.Equal("fallback-value", value);
        }

        [Fact]
        public void GetValue_returns_null_when_no_definition_found()
        {
            var organizationService = new Mock<IOrganizationService>();
            organizationService
                .Setup(service => service.RetrieveMultiple(It.IsAny<QueryExpression>()))
                .Returns(new EntityCollection());

            var value = EnvironmentVariableReader.GetValue(
                organizationService.Object,
                "shi_SomeFlag"
            );

            Assert.Null(value);
        }

        [Fact]
        public void GetValue_returns_null_when_org_service_is_null()
        {
            var value = EnvironmentVariableReader.GetValue(null, "shi_SomeFlag");

            Assert.Null(value);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void GetValue_returns_null_when_schema_name_is_blank(string schemaName)
        {
            var organizationService = new Mock<IOrganizationService>(MockBehavior.Strict);

            var value = EnvironmentVariableReader.GetValue(organizationService.Object, schemaName!);

            Assert.Null(value);
            organizationService.VerifyNoOtherCalls();
        }

        [Fact]
        public void GetValue_swallows_dataverse_errors_and_returns_null()
        {
            var organizationService = new Mock<IOrganizationService>();
            organizationService
                .Setup(service => service.RetrieveMultiple(It.IsAny<QueryExpression>()))
                .Throws(new InvalidOperationException("offline"));

            var value = EnvironmentVariableReader.GetValue(
                organizationService.Object,
                "shi_SomeFlag"
            );

            Assert.Null(value);
        }

        [Fact]
        public void GetValue_queries_using_provided_schema_name()
        {
            QueryExpression captured = null;
            var organizationService = new Mock<IOrganizationService>();
            organizationService
                .Setup(service => service.RetrieveMultiple(It.IsAny<QueryExpression>()))
                .Callback<QueryBase>(q => captured = q as QueryExpression)
                .Returns(new EntityCollection());

            EnvironmentVariableReader.GetValue(
                organizationService.Object,
                "shi_DisableInnerTraceDuplication"
            );

            Assert.NotNull(captured);
            Assert.Equal("environmentvariabledefinition", captured.EntityName);
            var condition = Assert.Single(captured.Criteria.Conditions);
            Assert.Equal("schemaname", condition.AttributeName);
            Assert.Equal("shi_DisableInnerTraceDuplication", condition.Values[0]);
        }

        [Fact]
        public void GetValue_with_ttl_caches_result_within_window()
        {
            var definition = new Entity("environmentvariabledefinition")
            {
                ["v.value"] = new AliasedValue("environmentvariablevalue", "value", "first"),
            };
            var organizationService = new Mock<IOrganizationService>();
            organizationService
                .Setup(s => s.RetrieveMultiple(It.IsAny<QueryExpression>()))
                .Returns(new EntityCollection(new[] { definition }));

            var first = EnvironmentVariableReader.GetValue(
                organizationService.Object,
                "shi_CacheTest",
                TimeSpan.FromMinutes(5)
            );
            var second = EnvironmentVariableReader.GetValue(
                organizationService.Object,
                "shi_CacheTest",
                TimeSpan.FromMinutes(5)
            );

            Assert.Equal("first", first);
            Assert.Equal("first", second);
            organizationService.Verify(
                s => s.RetrieveMultiple(It.IsAny<QueryExpression>()),
                Times.Once
            );
        }

        [Fact]
        public void GetValue_with_ttl_caches_null_results_too()
        {
            var organizationService = new Mock<IOrganizationService>();
            organizationService
                .Setup(s => s.RetrieveMultiple(It.IsAny<QueryExpression>()))
                .Returns(new EntityCollection());

            EnvironmentVariableReader.GetValue(
                organizationService.Object,
                "shi_MissingFlag",
                TimeSpan.FromMinutes(5)
            );
            EnvironmentVariableReader.GetValue(
                organizationService.Object,
                "shi_MissingFlag",
                TimeSpan.FromMinutes(5)
            );

            organizationService.Verify(
                s => s.RetrieveMultiple(It.IsAny<QueryExpression>()),
                Times.Once
            );
        }

        [Fact]
        public void GetValue_with_zero_ttl_skips_cache()
        {
            var definition = new Entity("environmentvariabledefinition")
            {
                ["v.value"] = new AliasedValue("environmentvariablevalue", "value", "x"),
            };
            var organizationService = new Mock<IOrganizationService>();
            organizationService
                .Setup(s => s.RetrieveMultiple(It.IsAny<QueryExpression>()))
                .Returns(new EntityCollection(new[] { definition }));

            EnvironmentVariableReader.GetValue(
                organizationService.Object,
                "shi_NoCache",
                TimeSpan.Zero
            );
            EnvironmentVariableReader.GetValue(
                organizationService.Object,
                "shi_NoCache",
                TimeSpan.Zero
            );

            organizationService.Verify(
                s => s.RetrieveMultiple(It.IsAny<QueryExpression>()),
                Times.Exactly(2)
            );
        }

        [Fact]
        public void ClearCache_forces_fresh_lookup()
        {
            var organizationService = new Mock<IOrganizationService>();
            organizationService
                .Setup(s => s.RetrieveMultiple(It.IsAny<QueryExpression>()))
                .Returns(new EntityCollection());

            EnvironmentVariableReader.GetValue(
                organizationService.Object,
                "shi_ClearTest",
                TimeSpan.FromMinutes(5)
            );
            EnvironmentVariableReader.ClearCache();
            EnvironmentVariableReader.GetValue(
                organizationService.Object,
                "shi_ClearTest",
                TimeSpan.FromMinutes(5)
            );

            organizationService.Verify(
                s => s.RetrieveMultiple(It.IsAny<QueryExpression>()),
                Times.Exactly(2)
            );
        }

        [Fact]
        public void GetValue_with_ttl_isolates_per_schema_name()
        {
            var organizationService = new Mock<IOrganizationService>();
            organizationService
                .Setup(s => s.RetrieveMultiple(It.IsAny<QueryExpression>()))
                .Returns(new EntityCollection());

            EnvironmentVariableReader.GetValue(
                organizationService.Object,
                "shi_FlagA",
                TimeSpan.FromMinutes(5)
            );
            EnvironmentVariableReader.GetValue(
                organizationService.Object,
                "shi_FlagB",
                TimeSpan.FromMinutes(5)
            );

            organizationService.Verify(
                s => s.RetrieveMultiple(It.IsAny<QueryExpression>()),
                Times.Exactly(2)
            );
        }
    }
}
