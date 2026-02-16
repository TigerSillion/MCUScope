# AGENT_MCP_SKILLS.md

## Purpose

This document defines repository-level execution rules for agent-driven development, including MCP usage, skills usage, and AI coding constraints.

## Agent Execution Rules

- Prefer direct execution and verification over repeated planning questions.
- Keep each change set scoped and push every commit to GitHub.
- Never include unrelated workspace changes in a commit.
- Keep change logs and changelog entries updated on every user-visible change.

## MCP Rules

- Prefer local workspace inspection first.
- Use MCP resources/templates when they provide direct task context.
- Keep MCP reads minimal and only fetch files required by the active task.
- If a required MCP resource is unavailable, fall back to local code + explicit note in `docs/DEV_LOG.md`.

## Skills Rules

- Trigger a skill when the user explicitly names it or when task scope clearly matches the skill description.
- Use the minimum skill set needed for the active turn.
- Resolve skill-relative paths from the skill directory first.
- If a skill is unavailable, continue with best-effort fallback and log the gap.

## AI Coding Constraints

- Enforce whitelist-based variable access on MCU communication paths.
- Keep protocol framing deterministic and bounds-checked.
- Avoid edits in `src/smc_gen/*` non-user sections.
- Add clear comments for non-obvious control/path logic.
- Update `docs/USAGE.md` and `docs/TESTING.md` when behavior changes.
- Record actual test status (run, skipped, blocked) in `docs/DEV_LOG.md`.

## Commit and Release Discipline

- One logical feature or fix per commit.
- Every commit must include:
  - code changes,
  - documentation updates,
  - a `docs/DEV_LOG.md` entry,
  - an updated `docs/CHANGELOG.md` entry when user-visible.
- Push each commit immediately after successful local checks.

