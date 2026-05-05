using System;
using Microsoft.Xrm.Sdk;
using Moq;
using SHI.CRM.Plugins.Base.Diagnostics;
using Xunit;

namespace BasePluginTests.Diagnostics
{
    public class TracingExtensionsTests
    {
        [Fact]
        public void TraceWithContext_is_noop_when_tracing_null()
        {
            var context = new Mock<IPluginExecutionContext>().Object;

            var ex = Record.Exception(() =>
                ((ITracingService)null).TraceWithContext(context, new Exception("oops"), "Label")
            );

            Assert.Null(ex);
        }

        [Fact]
        public void TraceWithContext_includes_label_in_trace_arguments()
        {
            var tracing = new Mock<ITracingService>();
            var context = new Mock<IPluginExecutionContext>();

            tracing.Object.TraceWithContext(
                context.Object,
                new InvalidOperationException(),
                "Label"
            );

            tracing.Verify(
                t =>
                    t.Trace(
                        It.IsAny<string>(),
                        It.Is<object[]>(args =>
                            args != null
                            && args.Length > 0
                            && args[0] != null
                            && args[0].ToString() == "Label"
                        )
                    ),
                Times.Once
            );
        }

        [Fact]
        public void TraceWithContext_includes_plugin_type_and_context_metadata()
        {
            var tracing = new Mock<ITracingService>();
            var context = new Mock<IPluginExecutionContext>();
            context.Setup(c => c.Stage).Returns(40);
            context.Setup(c => c.Depth).Returns(2);
            context.Setup(c => c.SecondaryEntityName).Returns("contact");

            tracing.Object.TraceWithContext(
                context.Object,
                new InvalidOperationException(),
                "Label",
                "My.Plugin"
            );

            tracing.Verify(
                t =>
                    t.Trace(
                        It.IsAny<string>(),
                        It.Is<object[]>(args =>
                            args != null
                            && args.Length >= 19
                            && args[0] != null
                            && args[0].ToString() == "Label"
                            && args[14] != null
                            && args[14].ToString() == "contact"
                            && args[15] != null
                            && args[15].ToString() == "0" // Mode default enum 0 when not set explicitly
                            && args[18] != null
                            && args[18].ToString() == "My.Plugin"
                        )
                    ),
                Times.Once
            );
        }

        [Fact]
        public void TraceWithContext_swallows_exceptions_from_tracing_implementation()
        {
            var tracing = new Mock<ITracingService>();
            tracing
                .Setup(t => t.Trace(It.IsAny<string>(), It.IsAny<object[]>()))
                .Callback(() => throw new InvalidOperationException("trace failed"));

            var ex = Record.Exception(() =>
                tracing.Object.TraceWithContext(
                    new Mock<IPluginExecutionContext>().Object,
                    new Exception(),
                    "Label"
                )
            );

            Assert.Null(ex);
        }
    }
}
