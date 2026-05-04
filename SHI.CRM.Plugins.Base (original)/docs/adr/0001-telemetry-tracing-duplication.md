# ADR 0001: Telemetry tracing duplication toggle

## Status
Accepted

## Context
- `TelemetryTracingService` mirrors platform tracing (`ITracingService`) to Application Insights.
- Certain environments experienced noisy or duplicate logs when both inner tracing and AI telemetry were emitted.
- We needed a minimal, non-breaking way to suppress inner tracing while keeping telemetry.
- Telemetry common properties now include `PluginType` and broader execution context (user/org IDs, stage/depth/mode, counts) to aid attribution and debugging without logging payloads.

## Decision
- Introduced a duplication flag on `TelemetryTracingService` (default: **true**).
- Base implementation reads environment variable `DISABLE_INNER_TRACE_DUPLICATION`.
  - If set to `1` (case-insensitive), inner tracing calls are skipped; AI telemetry continues when enabled.
- Telemetry remains best-effort and must never throw.

## Consequences
- Operators can reduce log noise without code changes by setting the env var.
- Derived plugins receive both `tracing` and `cloudTracing`; they may still log explicitly as needed.
- Behavior remains backward-compatible by default (duplication on).

## Alternatives considered
- Config-driven toggle in plugin registration: rejected (more deployment friction).
- Per-plugin flag via constructor parameter: deferred; env flag met the immediate need with no signature churn.
