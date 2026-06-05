# ECC for Codex CLI

This supplements the root `AGENTS.md` with Codex-specific guidance.

## Model Recommendations

| Task Type | Recommended Model |
|-----------|------------------|
| Routine coding, tests, formatting | o4-mini |
| Complex features, architecture | o3 |
| Debugging, refactoring | o4-mini |
| Security review | o3 |

## Skills Discovery

Skills are auto-loaded from `.agents/skills/`. Each skill contains:
- `SKILL.md` — Detailed instructions and workflow
- `agents/openai.yaml` — Codex interface metadata

Available skills (after ECC setup):
- `tdd-workflow` — Test-driven development with 80%+ coverage
- `security-review` — Comprehensive security checklist
- `coding-standards` — Universal coding standards
- `backend-patterns` — API design, database, caching
- `e2e-testing` — Playwright E2E tests
- `verification-loop` — Build, test, lint, typecheck, security
- `deep-research` — Multi-source research with firecrawl and exa MCPs
- `api-design` — REST API design patterns
- `eval-harness` — Eval-driven development
- `claude-api` — Anthropic Claude API patterns and SDKs

## MCP Servers

Treat the project-local `.codex/config.toml` as the Codex baseline. It enables:
GitHub, Context7, Exa, Memory, Playwright, and Sequential Thinking.

Add heavier extras in `~/.codex/config.toml` only when a task actually needs them.

### Environment Variables Required

```bash
export GITHUB_PERSONAL_ACCESS_TOKEN=ghp_...
export EXA_API_KEY=...
export OPENAI_API_KEY=sk-...
```

## Multi-Agent Support

Multi-agent workflows are enabled via `[features] multi_agent = true` in `.codex/config.toml`.

- Use `/agent` inside Codex CLI to inspect and steer child agents
- Three pre-configured roles: `explorer`, `reviewer`, `docs_researcher`

## Key Differences from Claude Code

| Feature | Claude Code | Codex CLI |
|---------|------------|-----------|
| Hooks | 8+ event types | Not yet supported |
| Context file | CLAUDE.md + AGENTS.md | AGENTS.md only |
| Skills | Skills loaded via plugin | `.agents/skills/` directory |
| Commands | `/slash` commands | Instruction-based |
| Agents | Subagent Task tool | Multi-agent via `/agent` |
| Security | Hook-based enforcement | Instruction + sandbox |
| MCP | Full support | Supported via `config.toml` |

## Security Without Hooks

Since Codex lacks hooks, security enforcement is instruction-based:
1. Always validate inputs at system boundaries
2. Never hardcode secrets — use environment variables
3. Run `npm audit` / `pip audit` before committing
4. Review `git diff` before every push
5. Use `sandbox_mode = "workspace-write"` in config

## External Action Boundaries

Require explicit user approval before posting, publishing, pushing, merging, opening paid jobs, dispatching remote agents, changing third-party resources, or modifying credentials. When approval is ambiguous, produce a local plan or draft instead.
