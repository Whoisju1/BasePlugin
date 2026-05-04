using System;
using Microsoft.Xrm.Sdk;
using Moq;
using SHI.CRM.Plugins.Base.Infrastructure;
using Xunit;

namespace BasePluginTests.Infrastructure
{
    public class PluginServiceResolverTests
    {
        private readonly Mock<IServiceProvider> _serviceProvider;
        private readonly Mock<ITracingService> _tracing;
        private readonly Mock<IPluginExecutionContext> _context;
        private readonly Mock<IOrganizationServiceFactory> _factory;
        private readonly Mock<IOrganizationService> _orgService;

        public PluginServiceResolverTests()
        {
            // Build baseline mocks for the service provider and CRM services used by PluginServiceResolver.
            _serviceProvider = new Mock<IServiceProvider>();
            _tracing = new Mock<ITracingService>();
            _context = new Mock<IPluginExecutionContext>();
            _factory = new Mock<IOrganizationServiceFactory>();
            _orgService = new Mock<IOrganizationService>();

            // IServiceProvider returns tracing, context, and factory in the happy path.
            _serviceProvider
                .Setup(sp => sp.GetService(typeof(ITracingService)))
                .Returns(_tracing.Object);
            _serviceProvider
                .Setup(sp => sp.GetService(typeof(IPluginExecutionContext)))
                .Returns(_context.Object);
            _serviceProvider
                .Setup(sp => sp.GetService(typeof(IOrganizationServiceFactory)))
                .Returns(_factory.Object);

            // Organization service factory produces an org service when given any user.
            _context.Setup(c => c.UserId).Returns(Guid.NewGuid());
            _factory
                .Setup(f => f.CreateOrganizationService(It.IsAny<Guid?>()))
                .Returns(_orgService.Object);
        }

        [Fact]
        public void Resolve_returns_services_when_all_dependencies_present()
        {
            var userId = Guid.NewGuid();
            _context.Setup(c => c.UserId).Returns(userId);

            var services = PluginServiceResolver.Resolve(_serviceProvider.Object);

            Assert.Same(_tracing.Object, services.Tracing);
            Assert.Same(_context.Object, services.Context);
            Assert.Same(_orgService.Object, services.OrganizationService);
            _factory.Verify(f => f.CreateOrganizationService(userId), Times.Once);
        }

        [Fact]
        public void Resolve_traces_and_throws_when_context_missing()
        {
            _serviceProvider
                .Setup(sp => sp.GetService(typeof(IPluginExecutionContext)))
                .Returns((IPluginExecutionContext)null);

            var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
                PluginServiceResolver.Resolve(_serviceProvider.Object)
            );

            Assert.Contains("context", ex.Message, StringComparison.OrdinalIgnoreCase);
            _tracing.Verify(t => t.Trace("Plugin execution context is unavailable."), Times.Once);
        }

        [Fact]
        public void Resolve_traces_and_throws_when_service_factory_missing()
        {
            _serviceProvider
                .Setup(sp => sp.GetService(typeof(IOrganizationServiceFactory)))
                .Returns((IOrganizationServiceFactory)null);

            var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
                PluginServiceResolver.Resolve(_serviceProvider.Object)
            );

            Assert.Contains("service factory", ex.Message, StringComparison.OrdinalIgnoreCase);
            _tracing.Verify(t => t.Trace("Organization service factory is unavailable."), Times.Once);
        }
    }
}
