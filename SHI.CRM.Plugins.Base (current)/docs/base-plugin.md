# Base Plugin Technical Guide

## Purpose
`BasePlugin` centralizes service resolution, tracing, Application Insights mirroring, and exception handling for Dynamics 365 CE plugins. Derivatives implement business logic only.

## Runtime wiring
1) CRM calls `BasePlugin.Execute(IServiceProvider)`.
2) `PluginServiceResolver` pulls:
   - `IPluginExecutionContext`
   - `IOrganizationService`
   - `ITracingService`
3) `TelemetryAdapter` resolves its Application Insights connection string in this order:
    1. Dataverse Environment Variable `shi_ApplicationInsightsConnectionString` (explicit value, then default value), read via `EnvironmentVariableReader` using `services.ExecutionService`.
    2. Host environment variable `APPLICATIONINSIGHTS_CONNECTION_STRING`.
    3. Host environment variable `APPINSIGHTS_CONNECTION_STRING`.

    If none of these resolve to a non-empty value, or the Application Insights SDK is unavailable, telemetry is disabled but platform tracing continues. Use the connection string from the Azure Application Insights resource, such as `InstrumentationKey=<guid>;IngestionEndpoint=https://<region>.in.applicationinsights.azure.com/`.
4) `TelemetryTracingService` wraps the platform tracer to mirror messages to Application Insights. Duplication of inner tracing defaults to **enabled** and can be disabled by setting any of:
    - Dataverse Environment Variable `shi_DisableInnerTraceDuplication` to `1`
    - Host environment variable `shi_DISABLE_INNER_TRACE_DUPLICATION=1`
    - Host environment variable `DISABLE_INNER_TRACE_DUPLICATION=1`

    The Dataverse value is checked first so platform admins can flip the flag without redeploying. The Dataverse lookup is cached per-process for 60 seconds (see `BasePlugin.DisableTraceFlagCacheTtl`), so high-volume plug-ins do not hit Dataverse on every invocation; flag changes take effect within the TTL window. Inner tracing is still forced on when telemetry is disabled so `cloudTracing` does not drop messages.
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
- To reduce log noise, set `DISABLE_INNER_TRACE_DUPLICATION=1` or `shi_DisableInnerTraceDuplication=1`. The inner tracer is skipped only when Application Insights telemetry is enabled.
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
- Blank/null connection string means telemetry is disabled.
- `TelemetryAdapter.GetOrCreate(orgService)` reuses an enabled adapter only while the resolved connection string is unchanged; disabled adapters are retried on later executions so a connection string added after the first call can still take effect.
- The Application Insights client is created with a dedicated telemetry configuration instead of mutating `TelemetryConfiguration.Active`.
- Completion trace: `BasePlugin.Execute completed` emits with `TotalDurationMs` for coarse timing.
- Metrics emitted (when telemetry is enabled): `InputParameterCount`, `SharedVariableCount`, and `TotalDurationMs` (ms). These land in `customMetrics` and are easier to chart/alert without casting.

### Configuration caching and test seams
- `EnvironmentVariableReader.GetValue` has two forms: an uncached lookup and a TTL-cached overload `GetValue(orgService, schemaName, TimeSpan cacheFor)`. The cache stores both hits and `null` results to avoid repeated round-trips for missing flags. `TimeSpan.Zero` skips the cache entirely.
- The `shi_DisableInnerTraceDuplication` flag is read through the cached overload with `BasePlugin.DisableTraceFlagCacheTtl` (60s). Flag changes from a Dataverse admin propagate within that window without a sandbox restart.
- `EnvironmentVariableReader.ClearCache()` is a test seam; the test project calls it from its test-class constructors so xUnit's unordered runs do not see a previous test's cached value.
- `BasePlugin.ResolveTelemetry(IOrganizationService)` is `internal virtual`. The default returns the singleton from `TelemetryAdapter.GetOrCreate`; tests inside the assembly (the test project shares the assembly via the projitems import) override the hook to inject a stub adapter without touching `TelemetryAdapter._instance` through reflection.

### Debugging with telemetry: which signal solves which problem

Every plug-in execution emits a structured event with the dimensions and metrics below. Each signal exists to answer a specific question that comes up during real-world debugging. This section explains **what each one is, why it's logged, the problem it solves, and how to use it to find a root cause.**

#### Identity and correlation signals

These are how you stitch a single user action together across many plug-in invocations and (if you have other instrumented services) across system boundaries.

- **`CorrelationId`** — Application Insights' end-to-end correlation identifier.
  - **Why it's logged:** lets you follow one user action across multiple services (web app → API → Dataverse plug-in → downstream).
  - **Problem it solves:** "A user clicked Submit and something went wrong somewhere — but where?" Filter by this ID and you see the entire chain in timestamp order.
  - **How to use it:** users (or your front-end) capture this ID; on a bug report, paste it into Application Insights and read the timeline.

- **`OperationId`** — groups every plug-in step fired by one Dataverse operation (pre-validate, pre-operation, post-operation, async).
  - **Why it's logged:** a single Create or Update can fire half a dozen plug-ins; this groups them.
  - **Problem it solves:** "The record looks wrong after save — which plug-in actually changed it?" Filter by `OperationId` and you see every plug-in stage in order.
  - **How to use it:** combine with `Stage` ordering to see which plug-in stage was the last one to run before the bad outcome.

- **`RequestId`** — Dataverse's internal pipeline request ID for this execution.
  - **Why it's logged:** lets you align Application Insights traces with Dataverse platform diagnostics, support tickets, or admin-center request logs.
  - **Problem it solves:** "Microsoft support gave us a request ID, but our App Insights queries use Application Insights IDs. How do we find this in our logs?" Filter by `RequestId`.
  - **How to use it:** mostly for cross-referencing with Microsoft support or platform tracing.

#### Pipeline shape signals

These tell you **where in the pipeline** a failure happened and **what was being done**.

- **`MessageName`** — the Dataverse message (Create/Update/Delete/Associate/SetState/Retrieve/etc.).
  - **Why it's logged:** the same plug-in class often handles different messages.
  - **Problem it solves:** "We have errors in this plug-in, but only some of them. Are they all the same operation?" Group by `MessageName` to find out.
  - **How to use it:** filter on the message you care about, e.g. `MessageName == 'Update'`, before slicing further.

- **`PrimaryEntityName` / `PrimaryEntityId`** — the entity and record being acted on.
  - **Why it's logged:** lets you find the trace for a specific record without logging the record's actual data.
  - **Problem it solves:** "Customer says record 12345 isn't saving correctly — what happened during their last attempt?" Filter by `PrimaryEntityId == '<guid>'`.
  - **How to use it:** start here when reproducing a customer-reported issue against a specific record.

- **`SecondaryEntityName`** — the paired entity for two-record messages (Associate, Disassociate, certain state changes).
  - **Why it's logged:** Associate/Disassociate involve two entities; logging only the primary loses half the context.
  - **Problem it solves:** "We have a relationship change failing — which side of the relationship is the issue on?"
  - **How to use it:** pair with `MessageName == 'Associate'` to investigate relationship-handling plug-ins.

- **`Stage`** — pipeline stage as a number: 10 = pre-validate, 20 = pre-operation, 40 = post-operation.
  - **Why it's logged:** identical plug-in code can register at multiple stages with very different responsibilities.
  - **Problem it solves:** "The plug-in works in pre-operation but fails in post — why?" Filter by `Stage`.
  - **How to use it:** use stage to narrow which plug-in registration is actually firing — pre-operation has different rules (e.g. you can mutate the Target) than post.

- **`Depth`** — pipeline recursion depth. `1` is a top-level invocation; `>1` means we're inside a re-entry.
  - **Why it's logged:** runaway recursion is a top-3 cause of plug-in incidents (a plug-in fires, makes a change, the change fires the same plug-in, and so on).
  - **Problem it solves:** "Why is this plug-in firing 50 times for one save?" Group by `Depth` and you'll see the recursion shape.
  - **How to use it:** any spike in events with `Depth > 1` for a plug-in that wasn't designed for re-entry is an alert-worthy signal.

- **`Mode`** — `0` is synchronous, `1` is asynchronous.
  - **Why it's logged:** the same plug-in class can be registered both sync and async with very different SLAs and failure modes.
  - **Problem it solves:** "Async plug-in failures look different from sync ones — am I comparing apples to oranges?" Filter by `Mode` first.
  - **How to use it:** when alerting or measuring p95 latency, partition by `Mode` so async background jobs don't drown out the synchronous user-facing path.

#### Identity-of-caller signals

These tell you **who** ran the plug-in and **on whose behalf**, which is essential for permission and impersonation issues.

- **`InitiatingUserId`** — the user who originated the request (the human at the keyboard).
  - **Why it's logged:** essential for audit and for diagnosing permission-sensitive behavior.
  - **Problem it solves:** "User X says they can't perform action Y — what does our log show actually happened when they tried?"
  - **How to use it:** filter by user ID + time window when investigating a user-reported permission denial.

- **`UserId`** — the identity the plug-in actually ran under (the step's run-as identity).
  - **Why it's logged:** plug-ins run as a registered identity, not as the initiating user. The two are usually different.
  - **Problem it solves:** "The plug-in succeeded for the system but the user said it failed — was it impersonation, or did our run-as identity have privileges the user didn't?" Compare `UserId` to `InitiatingUserId`.
  - **How to use it:** if these two differ and you see authorization-related errors, check whether your code used the right org service (`PermissionCheckService` vs `ExecutionService`).

- **`BusinessUnitId`** — the BU context the plug-in ran in.
  - **Why it's logged:** Dataverse RBAC is BU-scoped; a plug-in can succeed in BU A and fail in BU B with the same code.
  - **Problem it solves:** "Why does this work for users in HQ but break for the field team?" Group failures by `BusinessUnitId`.
  - **How to use it:** when bug reports cluster by region or team, this is the dimension to slice on.

- **`OrganizationId` / `OrganizationName`** — which Dataverse org/environment the event came from.
  - **Why it's logged:** when many environments (dev/test/UAT/prod, multi-tenant, customer orgs) feed the same Application Insights resource, you'd otherwise be unable to tell them apart.
  - **Problem it solves:** "We see errors but I can't tell if they're from prod or our test environment." Filter by org.
  - **How to use it:** always filter or partition by `OrganizationName` before reading totals — cross-environment numbers are nearly always misleading.

#### Volume and identity-of-code signals

- **`InputParameterCount`** — number of input parameters in the execution context (no names or values, just the count).
  - **Why it's logged:** volume changes are a leading indicator of misuse (suddenly twice as many parameters as last week) without leaking payload data.
  - **Problem it solves:** "A request started failing — was the request shape different from the ones that succeed?"
  - **How to use it:** chart the metric over time; spikes correlate with caller behavior changes upstream.

- **`SharedVariableCount`** — number of shared variables in flight.
  - **Why it's logged:** unbounded growth in shared variables suggests pipeline coupling that will scale poorly.
  - **Problem it solves:** "Plug-ins are slowing down over time — where's the coupling?"
  - **How to use it:** chart over time; if it climbs alongside latency, you have a cross-step coupling problem.

- **`PluginType`** — the fully qualified plug-in class name.
  - **Why it's logged:** multiple plug-ins are often registered against the same message/entity; this is how you tell them apart.
  - **Problem it solves:** "A specific plug-in is misbehaving but I can't isolate it from the others on the same Update message."
  - **How to use it:** filter by `PluginType` when investigating a known plug-in's behavior, or group by it to find which plug-in is the noisiest.

#### Runtime configuration signals

- **`TraceDuplicationEnabled`** — was platform tracing being duplicated to telemetry at the time of the event?
  - **Why it's logged:** to distinguish "we have no platform trace because telemetry is on and duplication is off" from "we have no platform trace because something is broken."
  - **Problem it solves:** "I'm not seeing the platform trace I expected for this execution — is that by design?" Check this flag.
  - **How to use it:** check this dimension before assuming a missing platform trace is a defect.

- **`TelemetryEnabled`** — was Application Insights enabled at the time of the event?
  - **Why it's logged:** a sandbox can run with telemetry disabled (no connection string, SDK missing). Knowing the state retroactively matters when you investigate gaps.
  - **Problem it solves:** "Why are we suddenly missing telemetry from environment X?" If `TelemetryEnabled == False` is appearing where it shouldn't, your connection string or SDK is gone.
  - **How to use it:** filter to `TelemetryEnabled == False` to find executions where Application Insights silently turned off.

#### Custom metrics

These land in `customMetrics` rather than `customDimensions` so they chart cleanly without parsing.

- **`TotalDurationMs`** — wall-clock duration of `BasePlugin.Execute`.
  - **Why it's logged:** the single most useful operational metric — answers "is the plug-in fast?"
  - **Problem it solves:** "Users say things feel slow." Chart p50/p95/p99 over time; sudden percentile shifts mean something changed.
  - **How to use it:** alert on p95 regressions per `PluginType`. This is what you'd page someone on.

- **`InputParameterCount`** / **`SharedVariableCount`** — counts at execution time, also emitted as metrics.
  - **Why it's logged as a metric:** so you can graph trends without writing KQL aggregation.
  - **Problem it solves:** see the dimension entries above.

### Debugging recipes

Concrete starting points. Each recipe matches a real-world investigation and shows the minimum viable KQL.

#### Recipe 1 — A user reported an error at a specific time

You usually have: a user, a time range, maybe a record ID. You want: every plug-in trace for that interaction.

```kusto
traces
| where timestamp between (datetime(2026-05-04 13:55) .. datetime(2026-05-04 14:05))
| where tostring(customDimensions.InitiatingUserId) == '{user-guid}'
| project timestamp, message, customDimensions.MessageName, customDimensions.PluginType, customDimensions.Stage, customDimensions.PrimaryEntityId
| order by timestamp asc
```

If you have a primary entity ID, drop the user filter and use `PrimaryEntityId` instead — usually a tighter match.

#### Recipe 2 — A plug-in is sometimes slow

```kusto
customMetrics
| where name == 'TotalDurationMs'
| where tostring(customDimensions.PluginType) == '{your.plugin.fully.qualified.name}'
| summarize p50 = percentile(value, 50), p95 = percentile(value, 95), p99 = percentile(value, 99), count()
    by bin(timestamp, 15m), tostring(customDimensions.Mode)
| order by timestamp asc
```

Comparing `Mode` partitions tells you whether the slowness is in the synchronous user path or the async background path.

#### Recipe 3 — Suspect runaway recursion

```kusto
traces
| where timestamp > ago(1h)
| where todouble(customDimensions.Depth) > 1
| summarize executions = count() by tostring(customDimensions.PluginType), tostring(customDimensions.OperationId)
| where executions > 5
| order by executions desc
```

Any `OperationId` with more than a handful of executions in the same plug-in usually means re-entry that wasn't intended.

#### Recipe 4 — Failure rate by environment

```kusto
traces
| where timestamp > ago(24h)
| where severityLevel >= 3
| summarize failures = count() by tostring(customDimensions.OrganizationName), tostring(customDimensions.PluginType)
| order by failures desc
```

Always filter or pivot by `OrganizationName` before you read totals — multi-org shared resources will mislead you otherwise.

#### Recipe 5 — Permission-sensitive bug suspected

```kusto
traces
| where customDimensions.PrimaryEntityId == '{record-guid}'
| project timestamp, message, customDimensions.UserId, customDimensions.InitiatingUserId, customDimensions.BusinessUnitId, customDimensions.PluginType
| order by timestamp asc
```

If `UserId != InitiatingUserId` and the trace shows an authorization-shaped failure, suspect that the plug-in chose the wrong org service identity for the call (`ExecutionService` instead of `PermissionCheckService`).

#### Recipe 6 — Find which stage broke a record

```kusto
traces
| where customDimensions.OperationId == '{op-guid}'
| project timestamp, customDimensions.PluginType, customDimensions.Stage, message
| order by timestamp asc
```

Read the timeline top to bottom: the last successful plug-in before the failure, plus the failed one, is your suspect window.

### Quick reference — telemetry dimensions

For a one-line lookup of every dimension's purpose:

- `CorrelationId`: End-to-end Application Insights correlation across services.
- `OperationId`: Groups all plugin steps fired by the same Dataverse operation.
- `RequestId`: The Dataverse pipeline request ID for this execution.
- `MessageName`: The pipeline message (Create/Update/Delete/Associate/SetState/etc.).
- `PrimaryEntityName` / `PrimaryEntityId`: The main entity and record.
- `SecondaryEntityName`: The paired/target entity for two-record messages.
- `Stage`: Plugin pipeline stage number (10/20/40).
- `Depth`: Recursion depth. Values >1 indicate re-entry.
- `Mode`: 0 = sync, 1 = async.
- `InitiatingUserId`: Who initiated the request.
- `UserId`: The identity the plugin executed under.
- `BusinessUnitId`: Business unit context for RBAC.
- `OrganizationId` / `OrganizationName`: Dataverse environment identifiers.
- `InputParameterCount`: Count of input parameters (no values logged).
- `SharedVariableCount`: Count of shared variables in flight.
- `PluginType`: Fully qualified plugin type name.
- `TraceDuplicationEnabled`: Whether platform tracing was duplicated at execution time.
- `TelemetryEnabled`: Whether Application Insights was enabled at execution time.

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
- Unit tests live in `SHI.CRM.Plugins.Base (current)/BasePluginTests`.
- Run from repo root:

```powershell
dotnet test "SHI.CRM.Plugins.Base (current)\SHI.CRM.Plugins.Base.slnx"
```

## Constraints and compatibility
- Targeted for existing CRM plugin assemblies; maintain compatibility with installed SDK versions.
- Telemetry is optional; plugin behavior should not depend on Application Insights availability.
