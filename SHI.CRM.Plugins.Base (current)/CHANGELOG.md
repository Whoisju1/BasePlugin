# Changelog

## Unreleased

- Updated `BasePlugin` exception handling so transient asynchronous plug-in failures are marked with `OperationStatus.Retry` and background failures use developer-oriented messages that point to plugin trace and telemetry.
- Removed the `PluginServices.OrganizationService` alias and refreshed implementation docs to use the current `PluginServices` contract.
