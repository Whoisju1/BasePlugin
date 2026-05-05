# Presenter Notes — BasePlugin Walkthrough

Personal notes for presenting `SHI.CRM.Plugins.Base` to the team and management. **Don't read these out — they're cues for you, not a script.**

Suggested length: 25–35 minutes including Q&A.

---

## Before the meeting

- Have both audience handouts ready (developer doc and executive summary).
- Open these tabs / files in advance:
  - `BasePlugin.cs` (so you can scroll the actual code if asked)
  - The Application Insights workspace (or a screenshot of it)
  - One example derived plug-in (real or sample) to show the "after" picture
- If your manager will be on the call, confirm whether they want technical depth or just business outcomes — adjust section weights accordingly.
- Decide upfront: are you asking for a decision today, or just informing? Phrase the closing slide accordingly.

---

## Section 1 — Open with the problem (3–4 min)

**Goal:** establish why this work matters before showing what it is.

Talking points:

- Plug-ins used to be repetitive. Same exception handling, same tracing, same service-resolution boilerplate copy-pasted across files.
- That repetition was a **bug surface** — every new plug-in was a chance to forget something or do it slightly wrong.
- Examples to drop in (use real ones if you have them):
  - A plug-in that leaked a raw stack trace to a user.
  - A plug-in that ran as the wrong identity and bypassed a permission check.
  - An async plug-in that failed once on a service-protection limit and never retried.

**Don't say:** "the old code was bad." **Do say:** "we kept solving the same problems individually instead of once."

---

## Section 2 — What the base actually does (5 min)

**Goal:** the elevator-pitch version of the feature list. Don't deep-dive yet.

Hit these in order:

1. Resolves Dataverse services for you (context, two org services, tracing).
2. Wraps unexpected exceptions so users never see stack traces — they get a reference ID instead.
3. Auto-retries transient async failures (service protection, timeouts).
4. Emits Application Insights telemetry with rich context: correlation IDs, entity, message, stage, depth, user IDs, plug-in type.
5. Two identities for org service calls — "step run-as" vs "initiating caller" — so authorization is explicit.

**If asked "is this Microsoft's base or ours?"** — Ours. Built on top of Microsoft's SDK.

**If asked "is this approach novel?"** — No. It's a well-known pattern (template method + service container). The value is having one canonical version instead of N copies.

---

## Section 3 — Live walk-through of a derived plug-in (5–8 min)

**Goal:** prove how simple it is to use.

Show the minimal example from the developer handout. Walk through it line by line:

1. Class inherits `BasePlugin`. **One line.**
2. Override `ExecutePluginLogic`. **One method.**
3. Use `services.Tracing` for diagnostics. **No setup.**
4. Use `services.PermissionCheckService` for permission checks (point out: "this honors the original caller, not the run-as identity").
5. Throw `InvalidPluginExecutionException` for business rule failures (point out: "this message reaches the user verbatim").
6. Use `services.ExecutionService` for the actual work.
7. Use `cloudTracing` for things you want in Application Insights too.

**Land this point hard:** there is no try/catch. No telemetry setup. No correlation ID plumbing. The base owns all of that.

**If a developer asks "what if I want to catch exceptions myself?"** — You can, for genuine business-rule branches. But never wrap unknown exceptions yourself — the base does it better.

---

## Section 4 — The one decision developers must make (3 min)

**Goal:** make the dual-identity model unforgettable.

This is the most important slide. Spend time on it.

The question: **which identity should this Dataverse call use?**

- The user who initiated the action? → `services.PermissionCheckService`
- The identity the step is registered to run as? → `services.ExecutionService`

**Why it matters:** if you check "can the caller see this record?" using the run-as identity, you've just bypassed the permission check. The base **forces** developers to choose, which makes this kind of bug visible in code review.

**Anticipated question:** "Why not have a default?" — Because a default would hide the choice. We made it explicit on purpose.

---

## Section 5 — Configuration without redeploys (3 min)

**Goal:** show operations and management that this is a flexible system.

Two settings live in Dataverse Environment Variables:

- `shi_ApplicationInsightsConnectionString` — controls whether telemetry flows.
- `shi_DisableInnerTraceDuplication` — controls whether platform traces are duplicated when telemetry is on.

Both can be flipped by an authorized Dataverse admin. The disable-trace flag is cached for 60 seconds so we don't hammer Dataverse, but changes take effect within a minute.

**Why this matters to ops:** noisy plug-in fills the logs at 2 AM? An admin can suppress the duplicate output without paging anyone or deploying anything.

**Why this matters to management:** what used to be a code change with deployment risk is now a configuration change.

---

## Section 6 — Telemetry and what it gives us (3–5 min)

**Goal:** show the operational payoff in Application Insights.

If you have access to a real workspace, demo it. Otherwise show a screenshot.

Key dimensions to highlight:

- `CorrelationId` — follow one user action across multiple plug-ins.
- `PluginType` — filter to a specific plug-in.
- `TotalDurationMs` — the metric you'd actually alert on.

Show one sample KQL query, ideally one that finds a specific request from a hypothetical support ticket. Land the point: "support sends us a reference ID, and we can find every plug-in execution for that operation in seconds."

---

## Section 7 — Gotchas (3 min)

**Goal:** preempt the "but what about..." questions.

Walk through these quickly. They're in the developer handout in full.

1. Wrong service identity is a security bug. Code review must catch it.
2. Don't wrap every exception yourself — let the base do it.
3. No blanket depth guards. Make depth handling specific.
4. Don't log payloads or PII. The base captures rich context already.
5. Telemetry is best-effort. The plug-in must work without it.
6. Avoid `ColumnSet(true)`.

**Don't dwell on these.** If someone wants to discuss one, do it after the meeting.

---

## Section 8 — Status and ask (2 min)

**Goal:** close with what's done, what's next, and what you need.

Status:

- Built, tested (63 tests, 100% passing), documented.
- Already in use in [list any current consumers, or say "ready for adoption"].
- All telemetry configuration moved to Dataverse Environment Variables in the most recent change.

What you're asking for (pick one or more):

- **For the team:** commitment to use the base for all new plug-ins.
- **For management:** sign-off on phasing in migration of existing plug-ins.
- **For ops / admins:** ownership of the two Dataverse Environment Variables.

---

## Q&A — likely questions and quick answers

| Question | One-line answer |
|----------|-----------------|
| What if Application Insights is down? | Telemetry no-ops; plug-in runs normally. |
| Can it slow plug-ins down? | Negligible — telemetry is async and the env-var lookup is cached. |
| What happens to existing plug-ins that don't use it? | They keep working. Migration is opt-in unless we decide otherwise today. |
| How do we test plug-ins built on it? | Mock the platform services with the same harness pattern the base uses. Sample tests in the repo. |
| Can we extend it without breaking everyone? | Yes — the base evolves additively. Breaking changes are deliberate and documented. |
| Who maintains it? | [your team / your name] — same place as the rest of our shared CRM code. |
| What if a plug-in needs a third identity? | It can build one explicitly via `IOrganizationServiceFactory`. The base's two identities cover the common cases. |
| Why not use Microsoft's plug-in registration tool features instead? | Those don't give us a code-level safety net. This base complements registration, doesn't replace it. |

---

## If you only have 5 minutes

Cut to:

1. **Problem:** plug-ins were repetitive; repetition meant inconsistency and bugs.
2. **Solution:** one base class that handles errors, telemetry, identity, retry — uniformly.
3. **Demo:** show the minimal derived plug-in. Land "no try/catch, no telemetry setup."
4. **Operational win:** Dataverse Environment Variables let admins toggle behavior without redeploys.
5. **Ask:** [whatever you're asking for].

---

## After the meeting

- Send the developer doc to engineers.
- Send the executive summary to managers.
- File any open questions and follow-ups in the team's tracker.
- If a decision was made, write it down and confirm by email.

---

## Personal reminders

- Speak to the audience in the room. If your manager is more interested in risk and cost, weight Sections 5 and 8. If the team is more interested in implementation, weight Sections 3 and 4.
- It's fine to say "I don't know — let me check" for anything you're not sure about. Better than guessing.
- The base is already proven (tests pass, no production issues). You're not selling a hypothesis; you're sharing a working tool.
