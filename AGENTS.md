# Simulator — Agent Instructions

> Powered by [ECC (Everything Claude Code)](https://github.com/affaan-m/ECC) v2.0.0-rc.1 — 63 specialized agents, 251 skills, automated workflows.

## Project Overview

<!-- Fill in once the project has code:
- What this project does
- Key technologies / frameworks
- How to run / build / test
-->

## Core Principles

1. **Agent-First Delegation** — Route domain tasks to specialized agents rather than handling them monolithically.
2. **Test-Driven Development** — Write tests before code. Maintain 80%+ coverage.
3. **Security Prioritization** — Validate all inputs. Never compromise security for convenience.
4. **Immutability** — Create new objects instead of modifying existing state.
5. **Planning Before Execution** — Map complex features before implementation.

## Key Agents (available after ECC plugin install)

| Agent | Role |
|-------|------|
| `planner` | Complex features and refactoring |
| `architect` | System design decisions |
| `tdd-guide` | Test-driven workflows |
| `code-reviewer` | Quality assurance |
| `security-reviewer` | Vulnerability detection |
| `database-reviewer` | PostgreSQL / Supabase optimization |
| `mle-reviewer` | ML pipeline validation |

Language-specific reviewers: Python, TypeScript, Rust, Java, Go, Kotlin, C++, F#.

## Development Workflow

1. Plan using the `planner` agent for any non-trivial feature
2. Implement test-first via the `tdd-guide` agent
3. Review immediately with the `code-reviewer` agent
4. Commit using Conventional Commits format (`feat:`, `fix:`, `chore:`, etc.)
5. Document architectural decisions in the project, not isolated scratch files

## Security Requirements (Before Every Commit)

- Validate all inputs at system boundaries
- Prevent SQL injection and XSS
- Verify authentication and authorization
- Implement rate limiting on public endpoints
- Ensure error messages don't leak sensitive data
- **NEVER hardcode secrets — use environment variables or a secret manager**
- Run `npm audit` / `pip audit` before committing

## Architectural Guidance

- Repository patterns with consistent API response envelopes
- Immutability throughout
- Small, focused files (200–400 lines; 800 line max)
- Organize by feature/domain, not file type

## External Action Boundaries

Treat networked tools as read-only by default. Search, inspect, and draft freely within the user's requested scope, but **require explicit user approval** before:
- Posting, publishing, or pushing
- Opening/merging PRs or issues
- Dispatching remote agents
- Modifying credentials or third-party resources

When approval is ambiguous, produce a local plan or draft artifact instead of taking the external action.

## MCP Servers (via `.codex/config.toml`)

| Server | Purpose |
|--------|---------|
| github | GitHub repository access |
| context7 | Up-to-date library docs |
| exa | Neural web / code search |
| memory | Persistent cross-session memory |
| playwright | Browser automation |
| sequential-thinking | Structured multi-step reasoning |

Required env vars: `GITHUB_PERSONAL_ACCESS_TOKEN`, `EXA_API_KEY`, `OPENAI_API_KEY`.
