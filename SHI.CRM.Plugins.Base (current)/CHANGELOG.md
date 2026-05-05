# Changelog

## Unreleased

- Updated `BasePlugin` exception handling so transient asynchronous plug-in failures are marked with `OperationStatus.Retry` and background failures use developer-oriented messages that point to plugin trace and telemetry.
- Removed the `PluginServices.OrganizationService` alias and refreshed implementation docs to use the current `PluginServices` contract.
- `TelemetryAdapter` now reads the Application Insights connection string from the Dataverse Environment Variable `shi_ApplicationInsightsConnectionString` first, falling back to host environment variables `APPLICATIONINSIGHTS_CONNECTION_STRING` and `APPINSIGHTS_CONNECTION_STRING`.
- `BasePlugin` now reads the inner-trace duplication flag from the Dataverse Environment Variable `shi_DisableInnerTraceDuplication` first, falling back to host environment variables `shi_DISABLE_INNER_TRACE_DUPLICATION` and `DISABLE_INNER_TRACE_DUPLICATION`.
- Added internal `EnvironmentVariableReader` helper for reading Dataverse Environment Variables (explicit value, then default value) with safe fallbacks.
- Telemetry is now disabled when the resolved Application Insights connection string is null, empty, or whitespace.
- `TelemetryAdapter.GetOrCreate` now reuses enabled adapters only while the resolved connection string is unchanged, and no longer lets a disabled adapter permanently block later configuration.
- `TelemetryAdapter` now creates a dedicated Application Insights telemetry configuration instead of mutating global `TelemetryConfiguration.Active`.
- `TelemetryTracingService` now preserves platform tracing when telemetry is disabled, even if inner trace duplication is disabled.
- Test coverage now includes blank connection-string behavior, adapter reuse, trace preservation, and deterministic singleton tests.
- Test project now pins `System.Security.Cryptography.Xml` to `10.0.7` and suppresses the intentional Dataverse SDK `NU1701` compatibility warnings.
- Added `BasePlugin.ResolveTelemetry` (`internal virtual`) so tests can inject a stub `TelemetryAdapter` without reflection on the singleton.
- `EnvironmentVariableReader.GetValue` now offers a TTL-based caching overload (and a `ClearCache` test seam) so hot-path callers avoid a Dataverse round-trip on every plug-in execution.
- `BasePlugin.ShouldDuplicateInnerTrace` now caches the `shi_DisableInnerTraceDuplication` lookup for 60 seconds. Admins flipping the flag pick up the change after the TTL expires.
