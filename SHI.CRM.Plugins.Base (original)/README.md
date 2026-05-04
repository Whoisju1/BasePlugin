# SHI.CRM.Plugins.Base

Shared base plumbing for Dynamics 365 CE plugins. It resolves core services, wires platform tracing to Application Insights (kept as separate assemblies, not IL-repacked), and normalizes exception handling so derived plugins can focus on business logic.

## What it provides
- Service resolution: pulls `IPluginExecutionContext`, `IOrganizationService`, and `ITracingService` from the CRM `IServiceProvider`.
- Telemetry bridge: mirrors traces to Application Insights via `TelemetryAdapter`/`TelemetryTracingService` (best-effort; never throws). App Insights DLLs must sit beside the plugin; we do **not** merge them to avoid breaking early-bound proxy types.
- Fault handling: traces and rethrows known CRM faults; wraps unexpected exceptions in a user-safe `InvalidPluginExecutionException`.
- Common telemetry properties: correlation IDs, message name, entity, stage, and depth are attached to telemetry events.

## When to use
Use this base class for any plugin assembly that needs consistent tracing/telemetry and minimal boilerplate when interacting with the CRM execution context.

## How to implement a plugin
1. Derive from `BasePlugin`.
2. Optionally pass unsecure/secure config strings to the base constructor if you use them in your plugin registration.
3. Implement `ExecutePluginLogic`:

```csharp
protected override void ExecutePluginLogic(
    IServiceProvider serviceProvider,
    IPluginExecutionContext context,
    IOrganizationService orgService,
    ITracingService tracing,
    ITracingService cloudTracing)
{
    // Use tracing for platform traces, cloudTracing to also mirror to Application Insights.
    // Put your business logic here.
}
```

### Tracing and telemetry
- `tracing` is the platform tracer (always available).
- `cloudTracing` wraps `tracing` and mirrors messages to Application Insights when an AI connection string is present. If AI is absent, it quietly no-ops.
- Common telemetry properties now include: correlation IDs, message name, entity info, stage/depth, mode, initiating/executing user IDs, business unit/organization IDs and name, input/shared variable counts, and `PluginType` so you can filter in AI. Runtime flags for `TraceDuplicationEnabled` and `TelemetryEnabled` are also attached.
- Inner trace duplication can be disabled by setting environment variable `DISABLE_INNER_TRACE_DUPLICATION=1`; telemetry still flows when available.
- Application Insights connection strings are read from `APPLICATIONINSIGHTS_CONNECTION_STRING` or `APPINSIGHTS_CONNECTION_STRING`.

### Error handling
- Throw `InvalidPluginExecutionException` for user-facing business errors. Other exceptions are traced and wrapped automatically.
- Organization service faults are traced with error codes before rethrowing.

## Testing
- Tests live in `Plugins/SHI.CRM.Plugins.Base/BasePluginTests`.
- Run the suite from the repo root:

```powershell
dotnet test "Plugins/SHI.CRM.Plugins.Base/BasePluginTests/BasePluginTests.csproj"
```

## Tips
- Keep plugin logic small; avoid deep nesting—use early returns.
- Prefer dependency injection via the service provider where possible instead of newing services inside plugins.
- Do not log secrets or PII in traces or telemetry.
