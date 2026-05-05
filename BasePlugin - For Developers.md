# BasePlugin — Developer Guide

A practical introduction to writing Dataverse (Dynamics 365) plug-ins on top of `SHI.CRM.Plugins.Base`.

---

## What is it?

A shared base class every Dataverse plug-in inherits from. It handles the boring parts — pulling services from the platform, wiring up tracing, attaching telemetry, normalizing exceptions — so you only write the business logic that's unique to each plug-in.

You override one method: `ExecutePluginLogic`. The base does the rest.

---

## What you get when you derive from it

- Two organization services (one for the step run-as identity, one for the initiating caller)
- Platform tracing (`services.Tracing`)
- Application Insights mirroring (`cloudTracing`) when configured
- Common telemetry properties on every event (correlation IDs, message, entity, stage, depth, mode, user IDs, plug-in type)
- Custom metrics: input/shared variable counts and total duration in ms
- Safe exception wrapping — unexpected exceptions never reach end users as raw stack traces
- Async retry classification — transient Dataverse faults are flagged with `OperationStatus.Retry` automatically

---

## Your first plug-in

```csharp
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SHI.CRM.Plugins.Base;
using SHI.CRM.Plugins.Base.Infrastructure;

public class MyAccountPlugin : BasePlugin
{
    protected override void ExecutePluginLogic(
        IServiceProvider serviceProvider,
        PluginServices services,
        ITracingService cloudTracing)
    {
        services.Tracing.Trace(
            "Starting MyAccountPlugin for {0}",
            services.Context.PrimaryEntityName);

        // Permission check — uses the original caller's identity
        var record = services.PermissionCheckService.Retrieve(
            services.Context.PrimaryEntityName,
            services.Context.PrimaryEntityId,
            new ColumnSet("ownerid"));

        if (record.GetAttributeValue<EntityReference>("ownerid") == null)
        {
            // Business rule failure — message reaches the user as-is
            throw new InvalidPluginExecutionException("This account has no owner.");
        }

        // The actual work — uses the step's run-as identity
        services.ExecutionService.Update(
            new Entity("account") { Id = record.Id });

        // cloudTracing writes to BOTH platform trace and Application Insights
        cloudTracing.Trace("Completed MyAccountPlugin");
    }
}
```

That's a complete plug-in. No try/catch. No telemetry setup. No correlation ID plumbing. The base owns all of that.

---

## The two organization services — the one decision that matters most

Every `Retrieve` / `RetrieveMultiple` / `Update` / `Create` you write has to choose between these two. Get this wrong and you have either a security bug or a permissions error.

| You want to... | Use this | Why |
|----------------|----------|-----|
| Decide whether the **original caller** is allowed to do this | `services.PermissionCheckService` | Honors the calling user's permissions, not the step's run-as identity. |
| Do the actual work the step is registered for | `services.ExecutionService` | Runs as the step's configured identity. |
| Read configuration the plug-in itself needs | `services.ExecutionService` | Configuration is operational, not caller-scoped. |

There is **no generic `OrganizationService` property**. This is intentional — every call site has to make the identity choice explicit so reviewers can spot mistakes.

---

## Tracing — two flavors

```csharp
services.Tracing.Trace("local diagnostic");           // platform only
cloudTracing.Trace("milestone for App Insights");     // platform + App Insights
```

Use platform tracing for chatty hot-path messages; reserve `cloudTracing` for things you'd actually want to see in Application Insights or alert on.

---

## Exceptions — let the base handle them

The base catches three categories differently:

| You throw / something throws | The base does | The user sees |
|-------------------------------|---------------|---------------|
| `InvalidPluginExecutionException` | Traces, sends to telemetry, **rethrows verbatim** | Your exact message |
| `FaultException<OrganizationServiceFault>` | Traces with the error code, wraps in a friendly message | "Something went wrong... Reference: abc12345" |
| Anything else (bugs, null refs, etc.) | Traces, wraps in a friendly message | "Something went wrong... Reference: abc12345" |

**Don't write `try / catch (Exception) / throw new InvalidPluginExecutionException(ex.Message, ex)`.** That bypasses the base's safe wrapping, async retry classification, and reference IDs. Just let exceptions bubble up.

For genuine business-rule failures (a real "no, you can't do this"), throw `InvalidPluginExecutionException` with a user-friendly message. That message will reach the end user.

---

## Async retry — automatic

If your plug-in runs async and Dataverse hits a transient failure (service-protection limit, timeout), the base wraps it with `OperationStatus.Retry`. Dataverse retries the System Job automatically. You don't write any retry logic.

---

## Configuration

Two settings are read from Dataverse Environment Variables (with host env-var fallback). Admins can change them without a redeploy.

### Application Insights connection string

Lookup order:

1. Dataverse Environment Variable: `shi_ApplicationInsightsConnectionString`
2. Host env var: `APPLICATIONINSIGHTS_CONNECTION_STRING`
3. Host env var: `APPINSIGHTS_CONNECTION_STRING`

Empty / missing means telemetry is disabled. Platform tracing still works.

### Disable inner-trace duplication

Set any of these to `1` to skip platform tracing while keeping App Insights flowing:

1. Dataverse Environment Variable: `shi_DisableInnerTraceDuplication`
2. Host env var: `shi_DISABLE_INNER_TRACE_DUPLICATION`
3. Host env var: `DISABLE_INNER_TRACE_DUPLICATION`

The Dataverse value is cached for 60 seconds so we don't query Dataverse on every plug-in execution. A flag change takes effect within a minute.

---

## Telemetry dimensions you can filter on

Every telemetry event includes:

- **Identity / correlation:** `CorrelationId`, `OperationId`, `RequestId`, `InitiatingUserId`, `UserId`, `BusinessUnitId`, `OrganizationId`, `OrganizationName`
- **Pipeline shape:** `MessageName`, `PrimaryEntityName`, `PrimaryEntityId`, `SecondaryEntityName`, `Stage`, `Depth`, `Mode`
- **Plug-in attribution:** `PluginType`
- **Volume hints:** `InputParameterCount`, `SharedVariableCount`
- **Runtime flags:** `TraceDuplicationEnabled`, `TelemetryEnabled`

Custom metrics emitted: `TotalDurationMs`, `InputParameterCount`, `SharedVariableCount`. They land in `customMetrics` and chart cleanly in KQL without casting.

### A few useful KQL starters

```kusto
// Follow one Dataverse operation across all plug-in stages
traces
| where customDimensions.OperationId == '{operation-guid}'
| order by timestamp asc

// Spot recursion loops
traces
| where todouble(customDimensions.Depth) > 1
| summarize count() by customDimensions.PluginType, customDimensions.MessageName

// Chart execution duration
customMetrics
| where name == 'TotalDurationMs'
| summarize avg(value), percentile(value, 95) by bin(timestamp, 1h)
```

---

## Testing your plug-in

Mock `IServiceProvider`, `IPluginExecutionContext`, `IOrganizationServiceFactory`, and the two org services, then call `plugin.Execute(serviceProvider)`. Assert against your business behavior — the base's behavior is already covered by its own tests.

If you need to inject a stub telemetry adapter (rare), there's an internal hook: override `ResolveTelemetry(IOrganizationService)` in your test plug-in.

---

## Gotchas — please internalize these

1. **Wrong service identity.** Using `ExecutionService` for a permission check that should reflect the original caller is a real security bug. Every read that decides "is the caller allowed?" must use `PermissionCheckService`.

2. **Wrapping every exception yourself.** Don't. Let unexpected exceptions bubble to the base — you'll get safe wrapping, async retry classification, and a correlation reference for free.

3. **Blanket `if (Depth > 1) return;` guards.** The base intentionally has no global depth guard because some legitimate flows re-enter the pipeline. Make depth handling specific to message + stage + expected business flow.

4. **Logging payloads, credentials, or PII.** The base already captures rich context. Your traces should add intent, not data.

5. **Assuming Application Insights is always available.** Telemetry is best-effort. Plug-in correctness must not depend on it. Platform tracing is the always-available baseline.

6. **`ColumnSet(true)`.** Retrieve only the columns the rule needs. Cheap habit, big impact at scale.

7. **Forgetting registration.** The base does not know your entity, message, stage, mode, filtering attributes, or images. Get the registration right first; the base only handles what happens after Dataverse fires the step.

---

## Quick reference

```csharp
// Pipeline metadata
services.Context.PrimaryEntityName
services.Context.PrimaryEntityId
services.Context.MessageName
services.Context.Stage
services.Context.Depth
services.Context.UserId
services.Context.InitiatingUserId
services.Context.CorrelationId

// Org services
services.ExecutionService          // step run-as identity
services.PermissionCheckService    // initiating caller identity

// Tracing
services.Tracing.Trace(...)        // platform only
cloudTracing.Trace(...)            // platform + App Insights

// Throwing user-facing errors
throw new InvalidPluginExecutionException("Friendly message");
```
