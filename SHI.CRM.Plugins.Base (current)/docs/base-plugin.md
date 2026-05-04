# Base Plugin Technical Guide

## Purpose
`BasePlugin` centralizes service resolution, tracing, Application Insights mirroring, and exception handling for Dynamics 365 CE plugins. Derivatives implement business logic only.

## Runtime wiring
1) CRM calls `BasePlugin.Execute(IServiceProvider)`.
2) `PluginServiceResolver` pulls:
   - `IPluginExecutionContext`
   - `IOrganizationService`
   - `ITracingService`
3) `TelemetryAdapter` loads Application Insights connection string from `APPLICATIONINSIGHTS_CONNECTION_STRING` or `APPINSIGHTS_CONNECTION_STRING`. If the connection string or Application Insights SDK is unavailable, telemetry is disabled but platform tracing continues.
4) `TelemetryTracingService` wraps the platform tracer to mirror messages to Application Insights. Duplication of inner tracing defaults to **enabled** and can be disabled by env var `DISABLE_INNER_TRACE_DUPLICATION=1`.
5) `ExecutePluginLogic` (implemented by derived classes) receives both tracers.
6) Exceptions:
    - `InvalidPluginExecutionException` is traced, then rethrown (user-facing business errors).
    - `FaultException<OrganizationServiceFault>` is traced with `ErrorCode`, then normalized via `PluginExceptionFactory.CreateUserSafeException`.
    - Any other exception is traced and wrapped via `PluginExceptionFactory.CreateUserSafeException`.
    - Async transient failures are converted to `InvalidPluginExecutionException` with `OperationStatus.Retry` so the async service can retry the System Job; async messages point developers to plugin trace and telemetry.
7) Telemetry is flushed best-effort on exit.

## Implementing a plugin
Start with the registration contract, then keep each identity choice explicit:

- use `services.Context` for pipeline metadata and input parameters
- use `services.PermissionCheckService` for permission-sensitive reads that must honor the initiating caller
- use `services.ExecutionService` for the work the step is configured to perform
- throw `InvalidPluginExecutionException` for expected business-rule failures
- let the base normalize unexpected exceptions, Dataverse faults, and async retry behavior

```csharp
protected override void ExecutePluginLogic(
    IServiceProvider serviceProvider,
    PluginServices services,
    ITracingService cloudTracing)
{
    services.Tracing.Trace("Starting MyPlugin for {0}", services.Context.PrimaryEntityName);
    cloudTracing.Trace("Starting MyPlugin with Application Insights mirroring");
    // Business logic here
}
```

### Tracing guidance
- Use `services.Tracing` for platform traces; use `cloudTracing` when you also want Application Insights mirroring.
- To reduce log noise, set `DISABLE_INNER_TRACE_DUPLICATION=1` (inner tracer skipped, Application Insights telemetry still flows when enabled).
- Avoid logging secrets/PII in either tracer.

### Service identity guidance
| Scenario | Service |
|----------|---------|
| Check whether the initiating caller can read or use a record | `services.PermissionCheckService` |
| Create or update records as part of this registered step | `services.ExecutionService` |
| Read plug-in configuration for operational behavior | `services.ExecutionService` |
| Decide whether a user-specific rule should allow the action | `services.PermissionCheckService` |

### Common mistakes
- Catching all exceptions in the derived plug-in and replacing the message with raw exception details.
- Using the step execution identity for permission checks that should honor the original caller.
- Adding a generic depth guard without understanding the registration and expected re-entry path.
- Logging sensitive payloads when the base already captures correlation, entity, stage, user, and mode context.
- Assuming Application Insights is required for correctness; telemetry is best-effort and platform tracing remains the baseline.

### Do and don't examples
The snippets below show patterns, not copy-paste-complete plug-ins. `ColumnSet` comes from `Microsoft.Xrm.Sdk.Query`; helper names such as `CallerCanUseRecord` and `RunBusinessLogic` stand in for the derived plug-in's own business logic.

#### Service identity
Don't use the step execution identity for a read that is really an authorization check:

```csharp
var target = services.ExecutionService.Retrieve(
    services.Context.PrimaryEntityName,
    services.Context.PrimaryEntityId,
    new ColumnSet("ownerid")
);
```

Do use the initiating caller identity when the result decides whether the original caller may continue:

```csharp
var target = services.PermissionCheckService.Retrieve(
    services.Context.PrimaryEntityName,
    services.Context.PrimaryEntityId,
    new ColumnSet("ownerid")
);

if (!CallerCanUseRecord(target, services.Context.InitiatingUserId))
{
    throw new InvalidPluginExecutionException(
        "The initiating user is not allowed to perform this action."
    );
}
```

#### Exception handling
Don't catch every exception and expose the raw exception message:

```csharp
try
{
    RunBusinessLogic(services);
}
catch (Exception ex)
{
    throw new InvalidPluginExecutionException(ex.Message, ex);
}
```

Do throw business-rule failures intentionally and let `BasePlugin` normalize unexpected failures:

```csharp
if (!isValidForThisBusinessRule)
{
    throw new InvalidPluginExecutionException(
        "This record does not meet the requirements for this action."
    );
}

RunBusinessLogic(services);
```

#### Depth checks
Don't add a blanket depth guard without knowing whether re-entry is valid for the registration:

```csharp
if (services.Context.Depth > 1)
{
    return;
}
```

Do make depth handling specific to the message, stage, and expected business flow:

```csharp
if (
    services.Context.MessageName == "Update"
    && services.Context.Stage == 40
    && services.Context.Depth > 2
)
{
    services.Tracing.Trace(
        "Skipping post-update logic because depth {0} is outside the expected range.",
        services.Context.Depth
    );
    return;
}
```

#### Tracing
Don't write payloads, credentials, tokens, or sensitive field values to trace output:

```csharp
services.Tracing.Trace("External request payload: {0}", requestBody);
```

Do trace safe identifiers and execution context that help a developer find the related platform trace or telemetry:

```csharp
services.Tracing.Trace(
    "Processing {0} {1} for message {2} at stage {3}.",
    services.Context.PrimaryEntityName,
    services.Context.PrimaryEntityId,
    services.Context.MessageName,
    services.Context.Stage
);
```

#### Column selection
Don't retrieve every column when the rule only needs a few values:

```csharp
var target = services.ExecutionService.Retrieve(
    services.Context.PrimaryEntityName,
    services.Context.PrimaryEntityId,
    new ColumnSet(true)
);
```

Do request only the columns the rule needs. Replace the example column names with the actual columns required by the plug-in step:

```csharp
var target = services.ExecutionService.Retrieve(
    services.Context.PrimaryEntityName,
    services.Context.PrimaryEntityId,
    new ColumnSet("ownerid", "statecode")
);
```

### Telemetry adapter behavior
- Resilient: errors in the Application Insights SDK are swallowed.
- `TelemetryAdapter.GetOrCreate()` is a singleton; reuse avoids repeated reflection.
- Completion trace: `BasePlugin.Execute completed` emits with `TotalDurationMs` for coarse timing.
- Metrics emitted (when telemetry is enabled): `InputParameterCount`, `SharedVariableCount`, and `TotalDurationMs` (ms). These land in `customMetrics` and are easier to chart/alert without casting.

#### Telemetry dimensions (what they mean and when to use)
- `CorrelationId`: End-to-end Application Insights correlation across services. Use when stitching a path that spans portal/API → plugin → downstream services.
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
- `OrganizationId` / `OrganizationName`: Environment identifiers; essential when multiple orgs feed the same Application Insights instance.
- `InputParameterCount`: Count of input parameters (no names/values logged). Spikes can flag unusually large requests.
- `SharedVariableCount`: Count of shared variables passed along the pipeline; growth can signal coupling across steps.
- `PluginType`: Fully qualified plugin type name. Use to filter to a specific plugin class when multiple plugins share a message/entity.

#### Sample KQL queries (Application Insights)

```kusto
// Follow one Dataverse operation (all plugin stages)
traces
| where customDimensions.OperationId == '{operation-guid}'
| order by timestamp asc

// Stitch across services using the Application Insights correlation
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
- Telemetry is optional; plugin behavior should not depend on Application Insights availability.
