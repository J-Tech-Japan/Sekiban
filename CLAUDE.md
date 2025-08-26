# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Language Guidelines

- This repository is OSS that people from many languages read it. please write all comment or document in English, unless we ask

## ⚠️ CRITICAL RULE FOR TYPESCRIPT DEVELOPMENT

**NEVER CREATE SIMPLIFIED IMPLEMENTATIONS THAT SKIP PROPER ACTOR IMPLEMENTATION.**

The Sekiban TypeScript implementation is under development. When you encounter missing features:
- DO NOT create in-memory workarounds
- DO NOT bypass the actor system
- DO NOT create "temporary" solutions
- DO ask about the proper approach
- DO help implement features properly

Every simplified implementation creates garbage code that must be completely rewritten.

## TypeScript Development Notes

- When executing TypeScript code and running tests, use typescript-build-tester for code execution and testing for dapr-sample projects

## Sekiban.slnx Development Overview

The solution file `Sekiban.slnx` groups together two main families of projects:

1. Sekiban.Pure* projects (stable / baseline framework evolution)
2. Sekiban.Dcb* projects (Dynamic Consistency Boundary implementation – actively evolving)

### Current Focus

Active feature and experimental work is concentrated in the `Sekiban.Dcb` line (including Postgres + Orleans integration). The goal is to enable Dynamic Consistency Boundary (DCB) semantics together with an actor model (Microsoft Orleans) so that dynamic query scopes + optimistic concurrency can be enforced with scalable grains.

### Test / Play Host

For day‑to‑day development and manual verification, run the host project:

`internalUsages/DcbOrleans.AppHost`

This host wires up:
- DCB core libraries
- Postgres persistence (current primary store for DCB scenario)
- Orleans silo + related services
- API + Web front ends

### Runtime Endpoints (when `DcbOrleans.AppHost` is running)

- Web UI (ASP.NET / Orleans integrated): http://localhost:5234/
- Scalar (OpenAPI / API explorer): http://localhost:5141/scalar/v1

If ports conflict locally they may auto-shift; confirm via the console output at launch (look for "Now listening on" lines). Keep documentation in sync if port numbers change in configuration.

### Contribution Guidance (DCB Scope)

- Prefer extending the DCB abstractions instead of introducing ad-hoc shortcuts.
- Keep actor (grain) boundaries explicit; avoid embedding cross-cutting logic directly inside web/API layers.
- When adding new query capabilities (multi-tag, composite event type filters) document minimal example usage so AI assistants can infer patterns.
- Any temporary instrumentation should be clearly marked and removed before merging.

### Quick Checklist Before Submitting DCB Changes

- [ ] New events / tags documented where they introduce new cross-entity invariants
- [ ] Optimistic concurrency expectations (dynamic query scope) validated with at least one test in `Sekiban.Dcb.*` test projects
- [ ] Actor lifecycle or placement changes justified in commit message
- [ ] Public surface (NuGet facing) reviewed for accidental breaking changes

This section exists so AI coding assistants understand the active development nucleus and choose the correct host + projects for context-aware changes.