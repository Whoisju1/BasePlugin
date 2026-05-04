using System;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Moq;
using SHI.CRM.Plugins.Base.Exceptions;
using Xunit;

namespace BasePluginTests.Exceptions
{
    public class PluginExceptionFactoryTests
    {
        [Fact]
        public void CreateUserSafeException_includes_reference_and_inner_exception()
        {
            string errMsg = "Something went wrong. Please try again. If this keeps happening, contact support with the reference number.";
            var correlation = Guid.NewGuid();
            var context = new Mock<IPluginExecutionContext>();
            context.Setup(c => c.CorrelationId).Returns(correlation);
            var inner = new InvalidOperationException("boom");

            var ex = PluginExceptionFactory.CreateUserSafeException(inner, context.Object);

            Assert.Same(inner, ex.InnerException);
            var expectedRef = correlation.ToString("N").Substring(0, 8);
            Assert.Contains(expectedRef, ex.Message);
            Assert.Contains(errMsg, ex.Message);
        }

        [Fact]
        public void CreateUserSafeException_omits_reference_when_correlation_missing()
        {
            var context = new Mock<IPluginExecutionContext>();

            // Null context behaves as missing correlation
            var exNullContext = PluginExceptionFactory.CreateUserSafeException(new Exception("x"), null);
            Assert.DoesNotContain("Reference", exNullContext.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("unknown", exNullContext.Message, StringComparison.OrdinalIgnoreCase);

            // If CorrelationId somehow yields an empty string (defensive), still returns unknown
            context.Setup(c => c.CorrelationId).Returns(Guid.Empty);
            var exEmptyGuid = PluginExceptionFactory.CreateUserSafeException(new Exception("x"), context.Object);
            Assert.DoesNotContain("Reference", exEmptyGuid.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("unknown", exEmptyGuid.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void CreateUserSafeException_uses_async_message_for_async_non_transient_failure()
        {
            var context = new Mock<IPluginExecutionContext>();
            context.Setup(c => c.Mode).Returns(1);
            var inner = new InvalidOperationException("boom");

            var ex = PluginExceptionFactory.CreateUserSafeException(inner, context.Object);

            Assert.Equal(OperationStatus.Failed, ex.Status);
            Assert.Same(inner, ex.InnerException);
            Assert.Contains("Background processing failed", ex.Message);
            Assert.Contains("Review plugin trace and telemetry", ex.Message);
            Assert.DoesNotContain(
                "Please try again",
                ex.Message,
                StringComparison.OrdinalIgnoreCase
            );
            Assert.DoesNotContain(
                "contact support",
                ex.Message,
                StringComparison.OrdinalIgnoreCase
            );
        }

        [Fact]
        public void CreateUserSafeException_requests_retry_for_async_timeout()
        {
            var context = new Mock<IPluginExecutionContext>();
            context.Setup(c => c.Mode).Returns(1);

            var ex = PluginExceptionFactory.CreateUserSafeException(
                new TimeoutException("temporary timeout"),
                context.Object
            );

            Assert.Equal(OperationStatus.Retry, ex.Status);
            Assert.Contains("marked for retry", ex.Message);
            Assert.Contains("Review plugin trace and telemetry", ex.Message);
            Assert.DoesNotContain(
                "Please try again",
                ex.Message,
                StringComparison.OrdinalIgnoreCase
            );
            Assert.DoesNotContain(
                "contact support",
                ex.Message,
                StringComparison.OrdinalIgnoreCase
            );
        }

        [Fact]
        public void CreateUserSafeException_requests_retry_for_async_service_protection_fault()
        {
            var context = new Mock<IPluginExecutionContext>();
            context.Setup(c => c.Mode).Returns(1);
            var fault = new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault
                {
                    ErrorCode = -2147015902,
                    Message = "Service protection limit exceeded.",
                }
            );

            var ex = PluginExceptionFactory.CreateUserSafeException(fault, context.Object);

            Assert.Equal(OperationStatus.Retry, ex.Status);
            Assert.Contains("marked for retry", ex.Message);
            Assert.Contains("Review plugin trace and telemetry", ex.Message);
            Assert.DoesNotContain(
                "contact support",
                ex.Message,
                StringComparison.OrdinalIgnoreCase
            );
        }
    }
}
