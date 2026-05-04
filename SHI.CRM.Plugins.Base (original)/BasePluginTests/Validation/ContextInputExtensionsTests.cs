using System;
using Microsoft.Xrm.Sdk;
using Moq;
using SHI.CRM.Plugins.Base.Validation;
using Xunit;

namespace BasePluginTests.Validation
{
    public class ContextInputExtensionsTests
    {
        private readonly ParameterCollection _inputs = new();
        private readonly Mock<IPluginExecutionContext> _context = new();

        public ContextInputExtensionsTests()
        {
            _context.SetupGet(c => c.InputParameters).Returns(_inputs);
        }

        [Fact]
        public void GetInputParameter_returns_default_when_missing_or_null()
        {
            Assert.Equal(default(string), ContextInputExtensions.GetInputParameter<string>(_context.Object, "missing"));

            _inputs["nullValue"] = null;
            Assert.Equal(default(int), ContextInputExtensions.GetInputParameter<int>(_context.Object, "nullValue"));
        }

        [Fact]
        public void GetInputParameter_returns_typed_value_when_present()
        {
            _inputs["count"] = 5;

            var result = ContextInputExtensions.GetInputParameter<int>(_context.Object, "count");

            Assert.Equal(5, result);
        }

        [Fact]
        public void GetInputParameter_throws_when_type_mismatch()
        {
            _inputs["count"] = "not an int";

            Assert.Throws<InvalidPluginExecutionException>(() =>
                ContextInputExtensions.GetInputParameter<int>(_context.Object, "count")
            );
        }

        [Fact]
        public void GetRequiredInputParameter_throws_when_missing_or_null()
        {
            Assert.Throws<InvalidPluginExecutionException>(() =>
                ContextInputExtensions.GetRequiredInputParameter<string>(_context.Object, "missing")
            );

            _inputs["nullValue"] = null;
            Assert.Throws<InvalidPluginExecutionException>(() =>
                ContextInputExtensions.GetRequiredInputParameter<string>(_context.Object, "nullValue")
            );
        }

        [Fact]
        public void GetRequiredInputParameter_returns_typed_value_when_present()
        {
            _inputs["count"] = 5;

            var result = ContextInputExtensions.GetRequiredInputParameter<int>(_context.Object, "count");

            Assert.Equal(5, result);
        }
    }
}
