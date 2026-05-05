# SHI.CRM.Plugins.Base

## Review Tier
Critical

## Purpose
`SHI.CRM.Plugins.Base` is the shared execution wrapper for Dataverse plug-ins in this solution. It centralizes the plumbing that every plug-in otherwise has to repeat:

- resolve the execution context, tracing service, and organization services from `IServiceProvider`
- separate the execution identity from the initiating-caller identity
- attach telemetry and common trace metadata
- normalize exception handling so user-facing failures stay safe and consistent

This module is a shared base library, not a directly registered plug-in step.

## Registration
| Property        | Value |
|-----------------|-------|
| Entity          | `N/A - shared base library` |
| Message         | `N/A - shared base library` |
| Stage           | `N/A - shared base library` |
| Mode            | `N/A - shared base library` |
| Filtering Attrs | `N/A - shared base library` |
| Pre-Image       | `N/A - shared base library` |
| Post-Image      | `N/A - shared base library` |

## Business Logic
Each derived plug-in now receives a `PluginServices` container instead of separate `context`, `orgService`, and `tracing` arguments. The execution flow is:

1. Resolve `IPluginExecutionContext`, `ITracingService`, and `IOrganizationServiceFactory` from the platform `IServiceProvider`.
2. Create `services.ExecutionService` using `context.UserId` so normal plug-in work runs under the step execution identity.
3. Create `services.PermissionCheckService` using `context.InitiatingUserId` so authorization checks can honor the original caller.
4. Reuse the execution service when `UserId` and `InitiatingUserId` are the same to avoid an unnecessary second proxy.
5. Wrap the platform tracer with `cloudTracing` so traces can also flow to Application Insights when configured.
6. Execute derived plug-in logic and normalize business faults, Dataverse faults, and unexpected exceptions.
7. Mark transient asynchronous failures with `OperationStatus.Retry` so Dataverse can retry the System Job.

## Public Contract
Derive from `BasePlugin` and implement `ExecutePluginLogic` using the new contract:

### Start Here When Building a Derived Plug-in
1. Confirm the plug-in registration first: entity, message, stage, mode, filtering attributes, and images. The base does not guess those details for you.
2. Use `services.Context` for pipeline metadata and input parameters.
3. Use `services.Tracing` for normal Dataverse trace output. Use `cloudTracing` when the same message should also be mirrored to Application Insights when telemetry is available.
4. Use `services.PermissionCheckService` for reads that answer "is the initiating caller allowed to do this?"
5. Use `services.ExecutionService` for the work the registered step is configured to perform, such as creating or updating records under the step execution identity.
6. Throw `InvalidPluginExecutionException` for business-rule failures that should stop the pipeline. Let the base handle unexpected exceptions and Dataverse service faults.

```csharp
protected override void ExecutePluginLogic(
    IServiceProvider serviceProvider,
    PluginServices services,
    ITracingService cloudTracing)
{
    services.Tracing.Trace("Starting business logic for {0}", services.Context.PrimaryEntityName);

    // Use the initiating caller identity for authorization-sensitive reads.
    var canProceed = true; // replace with a real permission check

    if (!canProceed)
    {
        throw new InvalidPluginExecutionException("The initiating user is not allowed to perform this action.");
    }

    // Use the execution identity for the actual work the step is configured to perform.
    services.ExecutionService.Update(new Entity(services.Context.PrimaryEntityName)
    {
        Id = services.Context.PrimaryEntityId
    });

    cloudTracing.Trace("Completed business logic for {0}", services.Context.PrimaryEntityName);
}
```

### Migration Guidance
Before this change, derived plug-ins implemented:

```csharp
protected override void ExecutePluginLogic(
    IServiceProvider serviceProvider,
    IPluginExecutionContext context,
    IOrganizationService orgService,
    ITracingService tracing,
    ITracingService cloudTracing)
```

Now they implement:

```csharp
protected override void ExecutePluginLogic(
    IServiceProvider serviceProvider,
    PluginServices services,
    ITracingService cloudTracing)
```

Migration mapping:

- `context` -> `services.Context`
- `tracing` -> `services.Tracing`
- `orgService` -> `services.ExecutionService`
- permission-sensitive reads or checks that should honor the original caller -> `services.PermissionCheckService`

`PluginServices.OrganizationService` has been removed. Use `ExecutionService` or `PermissionCheckService` explicitly so the identity choice stays visible in reviews.

### Service Identity Guidance
- `services.ExecutionService` uses the plug-in step execution identity (`context.UserId`). This is the service to use for the work the step is configured to carry out.
- `services.PermissionCheckService` uses the initiating caller identity (`context.InitiatingUserId`). Use this when access checks, ownership checks, or other permission-sensitive reads must reflect the original caller rather than the step run-as identity.
- When both identities are the same, the base reuses the same service instance for both properties.

Service choice examples:

| Scenario | Use | Why |
|----------|-----|-----|
| Read a target or related record only to decide whether the initiating caller may continue | `services.PermissionCheckService` | The read should honor the original user's access, not just the step run-as identity. |
| Create or update records as part of the registered plug-in behavior | `services.ExecutionService` | The step registration controls the identity used for the work the plug-in is responsible for. |
| Retrieve configuration used by the plug-in itself | `services.ExecutionService` | Configuration reads are part of the step's operational work unless the business rule specifically needs caller-scoped access. |
| Emit trace messages for diagnostics | `services.Tracing` or `cloudTracing` | Use platform tracing by default; use `cloudTracing` when Application Insights mirroring is useful. |

### Tracing and telemetry
- `services.Tracing` is the platform tracer and is always available inside the Dataverse sandbox.
- `cloudTracing` wraps `services.Tracing` and mirrors messages to Application Insights when a non-empty Application Insights connection string is present. If Application Insights is unavailable, `cloudTracing` still writes to platform trace so messages are not lost.
- Common telemetry properties include correlation IDs, message name, entity info, stage/depth, mode, initiating/executing user IDs, business unit/organization IDs and name, input/shared variable counts, and `PluginType`.
- Runtime flags for `TraceDuplicationEnabled` and `TelemetryEnabled` are attached to every telemetry event.

#### Telemetry configuration sources
The base looks up telemetry configuration from Dataverse Environment Variables first, then falls back to host environment variables. Dataverse takes precedence so platform admins can change behavior without redeploying.

- **Application Insights connection string**:
  1. Dataverse Environment Variable `shi_ApplicationInsightsConnectionString` (explicit value, then default value)
  2. Host env var `APPLICATIONINSIGHTS_CONNECTION_STRING`
  3. Host env var `APPINSIGHTS_CONNECTION_STRING`

  Telemetry is disabled when this resolves to null, empty, or whitespace. Use the connection string copied from the Azure Application Insights resource, for example `InstrumentationKey=<guid>;IngestionEndpoint=https://<region>.in.applicationinsights.azure.com/`.
- **Disable inner trace duplication** (set any to `1` to skip the platform tracer only while telemetry is enabled):
  1. Dataverse Environment Variable `shi_DisableInnerTraceDuplication`
  2. Host env var `shi_DISABLE_INNER_TRACE_DUPLICATION`
  3. Host env var `DISABLE_INNER_TRACE_DUPLICATION`

The Dataverse lookup uses `services.ExecutionService` (the step run-as identity). Failures to read Environment Variables are swallowed and the next fallback in the list is used. The telemetry adapter reuses an enabled client only while the resolved connection string is unchanged, so adding or rotating the connection string can take effect without waiting for a sandbox recycle. Disabled adapters are retried on later executions.

The disable-trace flag is read on every plug-in execution but cached per-process for 60 seconds (`BasePlugin.DisableTraceFlagCacheTtl`), so a hot-path plug-in won't hit Dataverse for the flag on every invocation. Admins flipping the flag pick up the change within the TTL window.

Tests can override telemetry resolution by subclassing and overriding `BasePlugin.ResolveTelemetry(IOrganizationService)` (declared `internal virtual`); the default implementation returns `TelemetryAdapter.GetOrCreate(orgService)`.

### Deferred concern
- `TraceWithContext` currently traces the full exception object to preserve detailed sandbox diagnostics. This is a deliberate tradeoff: it helps diagnosis, but full exception payloads can contain sensitive values. The team chose to document the concern now and revisit redaction or narrower logging later rather than changing the behavior in this refactor.

### Error Handling
- Throw `InvalidPluginExecutionException` for user-facing business errors.
- Organization service faults are traced with error codes before they are converted into safe plug-in exceptions.
- Unexpected synchronous exceptions are traced and wrapped in a user-safe `InvalidPluginExecutionException` with a correlation-based reference.
- Unexpected asynchronous exceptions use developer-oriented System Job wording that points to plugin trace and telemetry. Transient async failures, including Dataverse service protection faults and timeouts, are marked with `OperationStatus.Retry`.

## Dependencies
- `BasePlugin.cs` coordinates execution flow and exception normalization.
- `PluginServiceResolver.cs` resolves the dual-service identity model.
- `PluginServices.cs` carries the resolved services into derived plug-ins.
- `TelemetryAdapter.cs` and `TelemetryTracingService.cs` handle Application Insights integration.
- `ContextInputExtensions.cs` and `PluginExceptionFactory.cs` provide shared validation and exception helpers.
- Application Insights is optional and activated only when the runtime environment provides the SDK and connection string.

## Testing
- Test project: [BasePluginTests.csproj](BasePluginTests/BasePluginTests.csproj)
- Contract coverage: [BasePluginTests.cs](BasePluginTests/BasePluginTests.cs), [TestableChildPlugin.cs](BasePluginTests/TestableChildPlugin.cs)
- Resolver coverage: [PluginServiceResolverTests.cs](BasePluginTests/Infrastructure/PluginServiceResolverTests.cs)
- Supporting harness: [PluginTestHarness.cs](BasePluginTests/Common/PluginTestHarness.cs)

Run the suite from the repo root:

```powershell
dotnet test "SHI.CRM.Plugins.Base (current)\SHI.CRM.Plugins.Base.slnx"
```

Key scenarios covered:

- dual-service resolution when `UserId` and `InitiatingUserId` differ
- service reuse when both identities match
- propagation of `PluginServices` into derived plug-in logic
- business, Dataverse, unexpected, and async retry exception handling
- telemetry disabled/connection-string behavior, adapter reuse, trace preservation, and deterministic singleton behavior

## Gotchas & Decisions
- Architectural decision: [ADR 0002](../../docs/decisions/0002-expose-plugin-execution-and-permission-services.md)
- The base intentionally does **not** add a global depth guard. Recursion protection remains the responsibility of individual plug-ins because acceptable depth thresholds depend on each step’s registration and business flow.
- `PluginServices` intentionally has no generic `OrganizationService` alias. Use `ExecutionService` or `PermissionCheckService` so intent stays clear.
- Full exception tracing is intentionally preserved for now; treat it as a known tradeoff and avoid tracing additional sensitive payloads in derived code.

### Common Mistakes in Derived Plug-ins
- Do not use `ExecutionService` for authorization checks that should reflect the original caller. Use `PermissionCheckService` for those reads.
- Do not catch every exception in the derived plug-in and rethrow `InvalidPluginExecutionException` with the raw exception message. That bypasses the base's safe wrapping, async retry classification, and consistent diagnostics.
- Do not add a blanket `Depth > 1` guard without checking the step registration and expected pipeline behavior. Some valid business flows re-enter the pipeline.
- Do not assume Application Insights is always available. Telemetry is optional; platform tracing must still tell the story.
- Do not trace secrets, credentials, or sensitive payloads. The base already captures rich execution context, so derived plug-ins should keep extra traces focused and safe.
- Do not retrieve all columns when writing derived plug-in logic. Request only the columns needed for the rule being evaluated.

See [base-plugin.md](docs/base-plugin.md) for concrete do and don't code examples covering service identity, exception handling, depth checks, tracing, and column selection.
