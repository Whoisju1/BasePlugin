# Documentation Index

This folder hosts technical docs for `SHI.CRM.Plugins.Base`.

- [Base Plugin Guide](./base-plugin.md) — runtime wiring, telemetry behavior, do/don't examples for derived plug-ins.
- [ADR 0001: Telemetry tracing duplication](./adr/0001-telemetry-tracing-duplication.md) — the inner-trace duplication flag, its sources, and the per-process cache TTL.
- [Project README](../README.md) — public contract, migration guidance, service identity, and gotchas.
- [CHANGELOG](../CHANGELOG.md) — release notes for the unreleased and prior versions.

Telemetry common properties (set in `TelemetryAdapter.BuildCommonProperties`) include plugin attribution (`PluginType`), correlation IDs, message and entity info, stage/depth/mode, user/business-unit/organization IDs, and runtime flags `TraceDuplicationEnabled` and `TelemetryEnabled`. These flow into every Application Insights event and metric for easier filtering.
