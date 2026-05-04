using System;
using System.Net;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;

namespace SHI.CRM.Plugins.Base.Exceptions
{
    /// <summary>
    /// Builds user-safe plugin exceptions with correlation references.
    /// </summary>
    public static class PluginExceptionFactory
    {
        private const int AsynchronousExecutionMode = 1;
        private const int DefaultErrorCode = 0;
        private const int ServiceProtectionRequestsExceeded = -2147015902;
        private const int ServiceProtectionExecutionTimeExceeded = -2147015903;
        private const int ServiceProtectionConcurrentRequestsExceeded = -2147015898;
        private const string RetryAfterKey = "Retry-After";

        private const string GenericUserMessageWithReference =
            "Something went wrong. Please try again. If this keeps happening, contact support with the reference number.";

        private const string GenericUserMessageWithoutReference =
            "Something went wrong. Please try again. If this keeps happening, contact support.";

        private const string AsyncFailureMessageWithReference =
            "Background processing failed. Review plugin trace and telemetry for this execution.";

        private const string AsyncFailureMessageWithoutReference =
            "Background processing failed. Review plugin trace and telemetry for this execution.";

        private const string AsyncRetryMessageWithReference =
            "Background processing hit a temporary issue and was marked for retry. Review plugin trace and telemetry if retries continue.";

        private const string AsyncRetryMessageWithoutReference =
            "Background processing hit a temporary issue and was marked for retry. Review plugin trace and telemetry if retries continue.";

        /// <summary>
        /// Creates a safe plug-in exception, using async retry status for transient asynchronous failures.
        /// </summary>
        public static InvalidPluginExecutionException CreateUserSafeException(
            Exception ex,
            IPluginExecutionContext context
        )
        {
            var isAsynchronous = IsAsynchronous(context);
            var isTransient = IsTransient(ex);
            var message = CreateSafeMessage(context, isAsynchronous, isTransient);

            if (isAsynchronous && isTransient)
            {
                return new InvalidPluginExecutionException(
                    OperationStatus.Retry,
                    DefaultErrorCode,
                    message
                );
            }

            return new InvalidPluginExecutionException(message, ex);
        }

        private static string CreateSafeMessage(
            IPluginExecutionContext context,
            bool isAsynchronous,
            bool isTransient
        )
        {
            var correlationId = context?.CorrelationId ?? Guid.Empty;
            var hasReference = correlationId != Guid.Empty;
            var refId = hasReference ? correlationId.ToString() : "unknown";

            var message = GetBaseMessage(hasReference, isAsynchronous, isTransient);
            return hasReference ? $"{message} Reference: {refId}." : message;
        }

        private static string GetBaseMessage(
            bool hasReference,
            bool isAsynchronous,
            bool isTransient
        )
        {
            if (isAsynchronous && isTransient)
            {
                return hasReference
                    ? AsyncRetryMessageWithReference
                    : AsyncRetryMessageWithoutReference;
            }

            if (isAsynchronous)
            {
                return hasReference
                    ? AsyncFailureMessageWithReference
                    : AsyncFailureMessageWithoutReference;
            }

            return hasReference
                ? GenericUserMessageWithReference
                : GenericUserMessageWithoutReference;
        }

        private static bool IsAsynchronous(IPluginExecutionContext context) =>
            context != null && context.Mode == AsynchronousExecutionMode;

        private static bool IsTransient(Exception ex)
        {
            if (ex == null)
                return false;

            if (ex is TimeoutException)
                return true;

            var organizationFault = ex as FaultException<OrganizationServiceFault>;
            if (organizationFault != null)
                return IsTransientOrganizationServiceFault(organizationFault.Detail);

            var webException = ex as WebException;
            if (webException != null)
                return IsTransientWebException(webException);

            var communicationException = ex as CommunicationException;
            if (communicationException != null && !(communicationException is FaultException))
                return true;

            var aggregateException = ex as AggregateException;
            if (aggregateException != null)
            {
                foreach (var innerException in aggregateException.InnerExceptions)
                {
                    if (IsTransient(innerException))
                        return true;
                }

                return false;
            }

            if (ex.InnerException == null)
                return false;

            return IsTransient(ex.InnerException);
        }

        private static bool IsTransientOrganizationServiceFault(OrganizationServiceFault fault)
        {
            if (fault == null)
                return false;

            if (
                fault.ErrorCode == ServiceProtectionRequestsExceeded
                || fault.ErrorCode == ServiceProtectionExecutionTimeExceeded
                || fault.ErrorCode == ServiceProtectionConcurrentRequestsExceeded
            )
            {
                return true;
            }

            if (fault.ErrorDetails == null)
                return false;

            foreach (var detail in fault.ErrorDetails)
            {
                if (string.Equals(detail.Key, RetryAfterKey, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool IsTransientWebException(WebException ex)
        {
            switch (ex.Status)
            {
                case WebExceptionStatus.ConnectFailure:
                case WebExceptionStatus.ConnectionClosed:
                case WebExceptionStatus.KeepAliveFailure:
                case WebExceptionStatus.NameResolutionFailure:
                case WebExceptionStatus.PipelineFailure:
                case WebExceptionStatus.ProxyNameResolutionFailure:
                case WebExceptionStatus.ReceiveFailure:
                case WebExceptionStatus.RequestCanceled:
                case WebExceptionStatus.SendFailure:
                case WebExceptionStatus.Timeout:
                    return true;
                case WebExceptionStatus.ProtocolError:
                    var response = ex.Response as HttpWebResponse;
                    return response != null && IsRetryableHttpStatus(response);
                default:
                    return false;
            }
        }

        private static bool IsRetryableHttpStatus(HttpWebResponse response)
        {
            if (response == null)
                return false;

            var statusCode = (int)response.StatusCode;
            return statusCode == 429
                || response.StatusCode == HttpStatusCode.BadGateway
                || response.StatusCode == HttpStatusCode.ServiceUnavailable
                || response.StatusCode == HttpStatusCode.GatewayTimeout;
        }
    }
}
