# Base Plugin Technical Guide

## Purpose
`BasePlugin` centralizes service resolution, tracing, Application Insights mirroring, and exception handling for Dynamics 365 CE plugins. Derivatives implement business logic only.

## Runtime wiring
1) CRM calls `BasePlugin.Execute(IServiceProvider)`.
2) `PluginServiceResolver` pulls:
   - `IPluginExecutionContext`
   - `IOrganizationService`
   - `ITracingService`
3) `TelemetryAdapter` loads Application Insights connection string from `APPLICATIONINSIGHTS_CONNECTION_STRING` or `APPINSIGHTS_CONNECTION_STRING`. If missing or the AI SDK assemblies are absent (they ship side-by-side, not IL-repacked), telemetry is disabled but platform tracing continues.
4) `TelemetryTracingService` wraps the platform tracer to mirror messages to Application Insights. Duplication of inner tracing defaults to **enabled** and can be disabled by env var `DISABLE_INNER_TRACE_DUPLICATION=1`.
5) `ExecutePluginLogic` (implemented by derived classes) receives both tracers.
6) Exceptions:
   - `InvalidPluginExecutionException` is traced, then rethrown (user-facing business errors).
   - `FaultException<OrganizationServiceFault>` is traced with `ErrorCode`, then rethrown.
   - Any other exception is traced and wrapped via `PluginExceptionFactory.CreateUserSafeException`.
7) Telemetry is flushed best-effort on exit.

## Implementing a plugin
```csharp
protected override void ExecutePluginLogic(
    IServiceProvider serviceProvider,
    IPluginExecutionContext context,
    IOrganizationService orgService,
    ITracingService tracing,
    ITracingService cloudTracing)
{
    tracing.Trace("Starting MyPlugin");
    cloudTracing.Trace("Starting MyPlugin with AI");
    // Business logic here
}
```

### Tracing guidance
- Use `tracing` for platform traces; use `cloudTracing` when you also want AI mirroring.
- To reduce log noise, set `DISABLE_INNER_TRACE_DUPLICATION=1` (inner tracer skipped, AI still used when enabled).
- Avoid logging secrets/PII in either tracer.

### Telemetry adapter behavior
- Resilient: errors in AI SDK are swallowed.
- `TelemetryAdapter.GetOrCreate()` is a singleton; reuse avoids repeated reflection.
- Completion trace: `BasePlugin.Execute completed` emits with `TotalDurationMs` for coarse timing.
- Metrics emitted (when telemetry is enabled): `InputParameterCount`, `SharedVariableCount`, and `TotalDurationMs` (ms). These land in `customMetrics` and are easier to chart/alert without casting.

#### Telemetry dimensions (what they mean and when to use)
- `CorrelationId`: End-to-end App Insights correlation across services. Use when stitching a path that spans portal/API → plugin → downstream services.
- `OperationId`: Groups all plugin steps fired by the same Dataverse operation (pre/parent/post). Use when you need the full story of a single business operation.
- `RequestId`: The Dataverse pipeline request ID for this execution. Use to align with platform logs or support tickets.
- `MessageName`: The pipeline message (Create/Update/Delete/Associate/SetState/etc.). Filter to see only the message you care about.
- `PrimaryEntityName` / `PrimaryEntityId`: The main entity and record for the operation. Use to see which record was touched without logging payloads.
- `SecondaryEntityName`: The paired/target entity for two-record messages (e.g., associate/disassociate relationships, state changes).
- `Stage`: Plugin pipeline stage number. Use to spot where a failure occurred (pre-validate, pre-operation, post-operation, async).
- `Depth`: Recursion depth. Values >1 indicate re-entry; useful for detecting loops.
- `Mode`: Sync vs async execution. Filter when comparing latency or failure rates between modes.
- `InitiatingUserId`: Who initiated the request. Use for auditing and to correlate with upstream caller identity.
- `UserId`: The user executing the plugin (may differ due to impersonation). Compare with `InitiatingUserId` to detect impersonation scenarios.
- `BusinessUnitId`: Business unit context for RBAC checks; helps spot BU-scoped issues.
- `OrganizationId` / `OrganizationName`: Environment identifiers; essential when multiple orgs feed the same App Insights instance.
- `InputParameterCount`: Count of input parameters (no names/values logged). Spikes can flag unusually large requests.
- `SharedVariableCount`: Count of shared variables passed along the pipeline; growth can signal coupling across steps.
- `PluginType`: Fully qualified plugin type name. Use to filter to a specific plugin class when multiple plugins share a message/entity.

#### Sample KQL queries (Application Insights)

```kusto
// Follow one Dataverse operation (all plugin stages)
traces
| where customDimensions.OperationId == '{operation-guid}'
| order by timestamp asc

// Stitch across services using the App Insights correlation
traces
| where customDimensions.CorrelationId == '{correlation-guid}'
| project timestamp, message, customDimensions.MessageName, customDimensions.PluginType
| order by timestamp asc

// Locate a single Dataverse request (e.g., from support)
traces
| where customDimensions.RequestId == '{request-guid}'
| summarize count() by customDimensions.MessageName, customDimensions.Stage

// Spot potential recursion loops
traces
| where todouble(customDimensions.Depth) > 1
| summarize count() by customDimensions.PluginType, customDimensions.MessageName

// Compare async vs sync behavior
traces
| summarize failures = countif(severityLevel >= 3), total = count() by customDimensions.Mode
| extend failureRate = failures * 1.0 / total

// Chart execution duration (ms) from customMetrics
customMetrics
| where name == 'TotalDurationMs'
| summarize avgDurationMs = avg(value), p95DurationMs = percentile(value, 95) by bin(timestamp, 1h)
```

## Testing
- Unit tests live in `Plugins/SHI.CRM.Plugins.Base/BasePluginTests`.
- Run from repo root:

```powershell
dotnet test "Plugins/SHI.CRM.Plugins.Base/BasePluginTests/BasePluginTests.csproj"
```

## Constraints and compatibility
- Targeted for existing CRM plugin assemblies; maintain compatibility with installed SDK versions.
- Telemetry is optional; plugin behavior should not depend on AI availability. Keep App Insights assemblies deployed next to the plugin (do not ILMerge/ILRepack them) to preserve early-bound proxy metadata and avoid “unknown entity type” errors.
