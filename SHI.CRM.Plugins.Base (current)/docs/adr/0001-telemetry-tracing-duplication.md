# ADR 0001: Telemetry tracing duplication toggle

## Status
Accepted

## Context
- `TelemetryTracingService` mirrors platform tracing (`ITracingService`) to Application Insights.
- Certain environments experienced noisy or duplicate logs when both inner tracing and Application Insights telemetry were emitted.
- We needed a minimal, non-breaking way to suppress inner tracing while keeping telemetry.
- Telemetry common properties now include `PluginType` and broader execution context (user/org IDs, stage/depth/mode, counts) to aid attribution and debugging without logging payloads.
- Suppressing inner tracing must not drop `cloudTracing` messages when Application Insights is disabled or misconfigured.

## Decision
- Introduced a duplication flag on `TelemetryTracingService` (default: **true**).
- Base implementation reads Dataverse Environment Variable `shi_DisableInnerTraceDuplication`, then host environment variables `shi_DISABLE_INNER_TRACE_DUPLICATION` and `DISABLE_INNER_TRACE_DUPLICATION`.
  - If set to `1` (case-insensitive), inner tracing calls are skipped; Application Insights telemetry continues when enabled.
- If telemetry is disabled, `TelemetryTracingService` still calls the inner platform tracer even when duplication is disabled.
- The Dataverse lookup is cached per-process for `BasePlugin.DisableTraceFlagCacheTtl` (60 seconds). The flag is consulted on every `Execute`, but only one Dataverse `RetrieveMultiple` is issued per TTL window. Both hits and `null` results are cached.
- Telemetry remains best-effort and must never throw.

## Consequences
- Operators can reduce duplicate log noise without code changes by setting the Dataverse flag or host env var.
- Flag changes from a Dataverse admin take effect within the 60-second TTL window. This is the explicit trade-off for not paying a Dataverse round-trip on every plug-in execution. Host env vars are consulted only when the (cached) Dataverse lookup returns null; once a Dataverse value is cached, the host env vars do not override it until the entry expires.
- Derived plugins receive both `tracing` and `cloudTracing`; they may still log explicitly as needed.
- Behavior remains backward-compatible by default (duplication on).
- Platform trace remains the diagnostic baseline when Application Insights is missing, blank, or failing.

## Alternatives considered
- Config-driven toggle in plugin registration: rejected (more deployment friction).
- Per-plugin flag via constructor parameter: deferred; env flag met the immediate need with no signature churn.
