# Changelog

## Unreleased

- Updated `BasePlugin` exception handling so transient asynchronous plug-in failures are marked with `OperationStatus.Retry` and background failures use developer-oriented messages that point to plugin trace and telemetry.
- Removed the `PluginServices.OrganizationService` alias and refreshed implementation docs to use the current `PluginServices` contract.
- `TelemetryAdapter` now reads the Application Insights connection string from the Dataverse Environment Variable `shi_ApplicationInsightsConnectionString` first, falling back to host environment variables `APPLICATIONINSIGHTS_CONNECTION_STRING` and `APPINSIGHTS_CONNECTION_STRING`.
- `BasePlugin` now reads the inner-trace duplication flag from the Dataverse Environment Variable `shi_DisableInnerTraceDuplication` first, falling back to host environment variables `shi_DISABLE_INNER_TRACE_DUPLICATION` and `DISABLE_INNER_TRACE_DUPLICATION`.
- Added internal `EnvironmentVariableReader` helper for reading Dataverse Environment Variables (explicit value, then default value) with safe fallbacks.
