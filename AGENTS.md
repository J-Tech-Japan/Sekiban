# Sekiban Agent Setup

Use Aspire MCP whenever an Aspire AppHost is running in this repository. The MCP server is configured in the repo so Claude, Codex, and GitHub Copilot can attach from regular checkouts and git worktrees.

Use `./playwright-cli` for Playwright commands from the repo root or a worktree. By default it targets `dcb/internalUsages/DcbOrleans.Benchmark.Playwright`, and it can bootstrap `node_modules` in a fresh worktree when Playwright is not installed yet.

Pass `--project-dir <path>` or set `PLAYWRIGHT_CLI_PROJECT_DIR` when you need a different Playwright package that contains its own `package.json`.
