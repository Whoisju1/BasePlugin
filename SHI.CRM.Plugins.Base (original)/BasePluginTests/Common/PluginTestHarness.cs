using System;
using Microsoft.Xrm.Sdk;
using Moq;

namespace BasePluginTests.Common
{
    /// <summary>
    /// Shared setup for plugin tests to avoid duplicating service provider wiring.
    /// </summary>
    internal class PluginTestHarness
    {
        public PluginTestHarness()
        {
            ServiceProvider
                .Setup(sp => sp.GetService(typeof(ITracingService)))
                .Returns(Tracing.Object);
            ServiceProvider
                .Setup(sp => sp.GetService(typeof(IPluginExecutionContext)))
                .Returns(Context.Object);
            ServiceProvider
                .Setup(sp => sp.GetService(typeof(IOrganizationServiceFactory)))
                .Returns(Factory.Object);

            SetDefaultContext(Guid.NewGuid());

            Factory
                .Setup(f => f.CreateOrganizationService(It.IsAny<Guid?>()))
                .Returns(OrganizationService.Object);
        }

        public Mock<IServiceProvider> ServiceProvider { get; } = new();
        public Mock<ITracingService> Tracing { get; } = new();
        public Mock<IPluginExecutionContext> Context { get; } = new();
        public Mock<IOrganizationServiceFactory> Factory { get; } = new();
        public Mock<IOrganizationService> OrganizationService { get; } = new();

        public void ResetContextParameters()
        {
            Context.Setup(c => c.InputParameters).Returns((ParameterCollection)null);
            Context.Setup(c => c.SharedVariables).Returns((ParameterCollection)null);
        }

        public void SetDefaultContext(Guid userId)
        {
            Context.Setup(c => c.UserId).Returns(userId);
            Context.Setup(c => c.MessageName).Returns("Create");
            Context.Setup(c => c.PrimaryEntityName).Returns("account");
        }
    }
}
