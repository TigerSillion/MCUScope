# AI_RULES.md

Core policy
- Prefer action over questions. Only ask when blocked by missing facts that cannot be inferred safely.
- Keep behavior deterministic and document all assumptions.
- Treat docs/UART_PROTOCOL.md as the protocol source of truth.
- Treat docs/AGENT_MCP_SKILLS.md as the shared baseline for Agent/MCP/Skills coordination rules.

Code quality
- Add clear comments for non-obvious logic and protocol handling.
- Keep functions small and single purpose.
- Validate inputs and guard against buffer overruns.

Safety and access
- MCU variable access must use a whitelist table. No raw arbitrary address access.
- Do not modify src/smc_gen/ outside protected user regions.

Documentation and tests
- Update docs/USAGE.md and docs/TESTING.md when behavior changes.
- Record test status in docs/DEV_LOG.md even if tests are skipped.

Version control
- Make small, scoped commits.
- Push each commit to GitHub.
- Do not stage unrelated changes.

Traceability
- Every change must have a detailed entry in docs/DEV_LOG.md.
- Update docs/CHANGELOG.md for user-visible changes.
