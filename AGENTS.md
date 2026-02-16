# AGENTS.md

This file defines agent execution rules for this repository.

Scope
- Follow user instructions in the current session and the rules in docs/AI_RULES.md.
- Keep all new text ASCII unless the target file already uses non-ASCII.

MCP and skills
- If a skill is named or the task matches a listed skill description, open its SKILL.md and follow it.
- If a skill file is missing, proceed with the best available fallback and note the gap in docs/DEV_LOG.md.

SMC safety
- Never edit non-user regions under src/smc_gen/.
- Only modify /* Start user code ... */ protected regions in src/smc_gen/.
- When a smc_gen user region is changed, update docs/SMC_GEN_MODIFICATIONS.md.

Logging
- Every change must append a detailed entry to docs/DEV_LOG.md.
- Include purpose, files touched, behavior impact, and tests (or note not run).

GitHub sync
- Each change set is a separate commit.
- Push to the GitHub remote after every commit.
- Do not amend or rewrite history unless explicitly asked.
- If the worktree has unrelated changes, stage only the intended files.

Documentation
- Provide usage and testing docs for new features.
- Ensure protocol and integration changes are documented in docs/.
