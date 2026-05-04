using System;
using Microsoft.Xrm.Sdk;

namespace SHI.CRM.Plugins.Base.Exceptions
{
    /// <summary>
    /// Builds user-safe plugin exceptions with correlation references.
    /// </summary>
    public static class PluginExceptionFactory
    {
        private const string GenericUserMessageWithReference =
            "Something went wrong. Please try again. If this keeps happening, contact support with the reference number.";

        private const string GenericUserMessageWithoutReference =
            "Something went wrong. Please try again. If this keeps happening, contact support.";

        public static InvalidPluginExecutionException CreateUserSafeException(
            Exception ex,
            IPluginExecutionContext context
        )
        {
            var correlationId = context?.CorrelationId ?? Guid.Empty;
            var hasReference = correlationId != Guid.Empty;
            var refId = hasReference
                ? correlationId.ToString()
                : null;

            var message = hasReference
                ? $"{GenericUserMessageWithReference} Reference: {refId}."
                : GenericUserMessageWithoutReference;
            return new InvalidPluginExecutionException(message, ex);
        }
    }
}
