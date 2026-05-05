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
    }
}
