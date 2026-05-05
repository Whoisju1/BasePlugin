# BasePlugin — Executive Summary

## What it is

A shared foundation that every Dataverse (Dynamics 365) plug-in in our solution is built on top of. Instead of each plug-in re-implementing the same plumbing — error handling, observability, security context, retry logic — they all inherit it from a single, well-tested base.

In one sentence: **a safety net plus a productivity multiplier for plug-in development.**

---

## Why it matters

### It reduces production risk

Before this base existed, each plug-in had its own way of handling errors, tracing, and Dataverse failures. That meant inconsistent behavior, occasional raw stack traces leaking to end users, and a different debugging story for every plug-in.

With the base in place:

- End users **never** see raw stack traces. They get a friendly message with a unique reference ID. Support tickets cite that reference; the team finds the matching log immediately.
- Dataverse service-protection failures in async plug-ins are **automatically retried** by the platform — they no longer turn into stuck System Jobs that operations has to babysit.
- Every plug-in produces the same shape of telemetry, so the same monitoring dashboard works for all of them.

### It cuts development time

A new plug-in is roughly **80% smaller** than one written from scratch. The boring, easy-to-get-wrong parts — service resolution, identity handling, telemetry, exception wrapping, retry classification — are all done once, in the base, and inherited for free.

This means:

- New plug-ins ship faster.
- Code reviews are shorter and more focused (reviewers look at business logic, not boilerplate).
- Onboarding a new developer onto plug-in work is materially easier.

### It gives operations real levers

Two operational behaviors can now be flipped by a Dataverse admin **without redeploying anything**:

1. **Application Insights connection.** Set once in a Dataverse Environment Variable; every plug-in starts emitting telemetry. Change it later if we rotate the Application Insights resource.
2. **Trace duplication.** When a noisy plug-in is filling our logs, an admin can suppress the duplicate platform-trace output while keeping Application Insights data flowing. The change takes effect within one minute.

This converts what used to be code changes (+ deployment + downtime risk) into configuration changes that any authorized admin can make.

### It enforces a security best practice

Plug-ins now have **two distinct identities** to choose from at every Dataverse call:

- The identity the plug-in is registered to run as (for the actual work).
- The identity of the user who initiated the request (for permission checks).

The base intentionally **forces** developers to choose between them on every call. This makes a class of subtle authorization bugs visible in code review — bugs that previously could and did slip through.

---

## What it does not do

Worth being explicit about:

- It does not deploy plug-ins or manage their registration.
- It does not eliminate the need for code review or testing — it makes both more effective.
- It is not a replacement for Application Insights monitoring or alerting; it is the source that feeds them.
- It is not Dataverse-specific magic; it's just a well-designed C# base class.

---

## Investment and ongoing cost

- The base is already built, tested (63 automated tests, all passing), and documented.
- Maintenance is light: small targeted changes, each commit accompanied by tests and documentation updates.
- The library has zero runtime dependencies beyond what plug-ins already need (Microsoft.Xrm.Sdk and an optional Application Insights SDK).
- Telemetry is fully optional — if Application Insights is unavailable or misconfigured, plug-ins still run normally; they just don't emit telemetry.

---

## What the team needs from you

- **Buy-in to make the base mandatory** for new plug-ins going forward. The benefits compound as more plug-ins use it.
- **A decision on whether to migrate existing plug-ins** that were written before the base existed. This is straightforward but takes engineering time; we can phase it.
- **Awareness that two Dataverse Environment Variables now drive operational telemetry behavior.** Whoever owns Dataverse environment configuration should know they exist.

---

## The bottom line

| Outcome | Impact |
|---------|--------|
| Faster plug-in delivery | Less boilerplate per plug-in, shorter reviews |
| Fewer production surprises | Friendly error messages, auto-retry on transient faults, consistent telemetry |
| Lower operational risk | Toggle telemetry and tracing in Dataverse, no redeploy |
| Better security posture | Forced identity choice prevents a class of authorization bugs |
| Easier debugging and support | Every plug-in surfaces the same correlation context |

This is foundational work — the kind that pays off every time we build, debug, or operate a plug-in for as long as we use Dataverse.
