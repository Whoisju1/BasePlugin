using System;
using Microsoft.Xrm.Sdk;

namespace SHI.CRM.Plugins.Base.Diagnostics
{
    /// <summary>
    /// Tracing helpers for CRM plugins.
    /// </summary>
    public static class TracingExtensions
    {
        public static void TraceWithContext(
            this ITracingService tracing,
            IPluginExecutionContext context,
            Exception ex,
            string label
        ) => TraceWithContext(tracing, context, ex, label, null);

        public static void TraceWithContext(
            this ITracingService tracing,
            IPluginExecutionContext context,
            Exception ex,
            string label,
            string pluginType
        )
        {
            try
            {
                tracing?.Trace(
                    "{0}. CorrelationId:{1} OperationId:{2} RequestId:{3} Message:{4} Stage:{5} Entity:{6} Id:{7} Depth:{8} User:{9} InitiatingUser:{10} BusinessUnit:{11} Org:{12}|{13} SecondaryEntity:{14} Mode:{15} InputCount:{16} SharedCount:{17} PluginType:{18} Details:{19}",
                    label,
                    context?.CorrelationId,
                    context?.OperationId,
                    context?.RequestId,
                    context?.MessageName,
                    context?.Stage,
                    context?.PrimaryEntityName,
                    context?.PrimaryEntityId,
                    context?.Depth,
                    context?.UserId,
                    context?.InitiatingUserId,
                    context?.BusinessUnitId,
                    context?.OrganizationId,
                    context?.OrganizationName,
                    context?.SecondaryEntityName,
                    context?.Mode,
                    context?.InputParameters?.Count,
                    context?.SharedVariables?.Count,
                    pluginType,
                    // TODO: We intentionally trace the full exception today to preserve sandbox diagnostics.
                    // This is a documented tradeoff for the shared base: it improves diagnosis, but the payload can
                    // contain sensitive values. Revisit narrowing or redacting this once the team agrees on a policy.
                    ex
                );
            }
            catch
            {
                // Ignore tracing failures to avoid hiding the underlying plugin exception.
            }
        }
    }
}
