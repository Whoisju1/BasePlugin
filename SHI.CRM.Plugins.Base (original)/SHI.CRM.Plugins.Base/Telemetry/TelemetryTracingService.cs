using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;

namespace SHI.CRM.Plugins.Base.Telemetry
{
    /// <summary>
    /// Wraps an <see cref="ITracingService"/> and forwards traces to Application Insights when enabled.
    /// Inner tracing duplication can be disabled (while still emitting telemetry) to avoid noisy inner logs.
    /// Telemetry is best-effort and never throws.
    /// </summary>
    internal sealed class TelemetryTracingService : ITracingService
    {
        private readonly ITracingService _inner;
        private readonly TelemetryAdapter _telemetry;
        private readonly IReadOnlyDictionary<string, string> _commonProps;
        private readonly bool _duplicateInnerTrace;

        /// <summary>
        /// Creates a tracing wrapper that mirrors all traces to Application Insights using the provided common properties.
        /// </summary>
        /// <param name="inner">The platform tracing service (invoked only when duplication is enabled).</param>
        /// <param name="telemetry">Telemetry adapter; no-ops if disabled.</param>
        /// <param name="commonProps">Shared telemetry properties cloned per call to avoid mutation.</param>
        /// <param name="duplicateInnerTrace">If false, skips calling the inner tracer while still sending telemetry.</param>
        public TelemetryTracingService(
            ITracingService inner,
            TelemetryAdapter telemetry,
            IReadOnlyDictionary<string, string> commonProps,
            bool duplicateInnerTrace = true
        )
        {
            _inner = inner;
            _telemetry = telemetry;
            _commonProps = commonProps;
            _duplicateInnerTrace = duplicateInnerTrace;
        }

        /// <summary>
        /// Traces to the inner service and mirrors to telemetry when enabled. Telemetry failures are swallowed.
        /// </summary>
        /// <param name="format">Format string for the message; null or whitespace yields an empty telemetry message.</param>
        /// <param name="args">Format arguments; ignored when null or empty.</param>
        public void Trace(string format, params object[] args)
        {
            if (_duplicateInnerTrace)
            {
                _inner?.Trace(format, args);
            }

            if (_telemetry == null || !_telemetry.IsEnabled)
                return;

            try
            {
                var message = BuildMessage(format, args);
                var props = CloneProps(_commonProps);
                _telemetry.TrackTrace(message, props);
            }
            catch
            {
                // Telemetry is best-effort; never block tracing.
            }
        }

        /// <summary>
        /// Builds a trace message from the format/args pair without throwing for null/empty formats.
        /// </summary>
        private static string BuildMessage(string format, object[] args)
        {
            // Defensive: null/blank format yields an empty message instead of throwing.
            if (string.IsNullOrWhiteSpace(format))
                return string.Empty;

            // Avoid string.Format when there are no args to reduce unnecessary allocations.
            if (args == null || args.Length == 0)
                return format;

            return string.Format(format, args);
        }

        /// <summary>
        /// Returns a shallow copy of telemetry properties to prevent downstream mutation of shared state.
        /// </summary>
        private static Dictionary<string, string> CloneProps(
            IReadOnlyDictionary<string, string> source
        )
        {
            // Make a shallow copy so downstream enrichment cannot mutate the shared dictionary.
            if (source == null)
            {
                return new Dictionary<string, string>();
            }

            var clone = new Dictionary<string, string>(source.Count);
            foreach (var kvp in source)
            {
                clone[kvp.Key] = kvp.Value;
            }

            return clone;
        }
    }
}
