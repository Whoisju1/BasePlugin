using System;
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
            var expectedRef = correlation.ToString();
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
    }
}
