# Scratchpad – multi-agent workspace

Shared working area for parallel agents. Nothing here ships; source code goes under `src/` and `tests/`.
The plan lives in `../PLAN.md`; this folder is for coordination.

## Layout

```
scratchpad/
  README.md        # this file: rules and protocol
  TASKS.md         # task board: one row per task, with owner and status
  DECISIONS.md     # decisions and their reasons, append-only
  OPEN_QUESTIONS.md# questions that need the user
  agents/<name>/   # private notes and drafts per agent
  handoffs/        # one file per finished task: what changed, what's next
  findings/        # research results (API notes, device dumps, test output)
```

## Rules

1. **Claim before working.** Set the owner and `in-progress` in `TASKS.md` before starting a task.
2. **One writer per file.** Only the task owner edits the files listed for that task. Shared files (`TASKS.md`, `DECISIONS.md`) are edited with small, targeted changes; re-read before editing.
3. **Stay in your lane.** Project boundaries: `Core` has no Win32 or UI types; `Win32` implements Core's interfaces; `App` depends on Core only (plus Win32 for composition root).
4. **Hand off in writing.** When done, add `handoffs/<task-id>.md` (files touched, how to verify, known gaps) and mark the task `done`.
5. **No device toggling in automated runs.** Only the user runs enable/disable against real hardware. Agents use fakes and read-only enumeration.
6. **No git operations.** The repo does not exist yet; do not `git init` or commit.
7. **Record decisions.** Anything non-obvious goes in `DECISIONS.md` with a date and reason; anything needing the user goes in `OPEN_QUESTIONS.md`.
8. **Keep it temporary.** Delete stale notes from `agents/` and `findings/` once their content is in code, tests, or `PLAN.md`.

## Parallelisation guide

After Phase 1 (scaffold) lands, these can run in parallel:

- Core models, `IDeviceService`, `DeviceSafetyPolicy`, `DisabledStateStore` + tests
- Win32 `DeviceEnumerator` + `HidUsageReader` (read-only)
- WPF shell and view models against a fake `IDeviceService`

`DeviceToggler` (Phase 4) starts only after the safety policy and enumeration are merged.
