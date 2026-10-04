# CinnabarSharp — Automation scripts (`--run`)

Goal: run a text file of image operations without a window, on one or several files:

```bash
cinnabarsharp --run script.txt [--input <file or glob>]... [--allow <folder>]...
```

Two uses:
1. **For users**: batch work (convert a folder of HEIC to JPEG, resize for the web, apply a filter to many photos).
2. **For releases**: the release workflow runs a test script with the **packaged Native AOT binary** on every OS, so a Windows, Linux or macOS build that can't open, edit or save an image fails the release instead of reaching users (performance-tasks.md P6 left Windows untested, and Linux only checked by hand).

Not covered: the GUI (dialogs, menus, canvas). `--run` is headless like `--mcp`. Running a script in the app (File › Run Script…) is for later.

## Design

**The commands are the MCP tools.** A script line is a tool name from `ImageTools` (`open_image`, `apply_effect`, `save_image`…) followed by its parameters. The runner starts the same MCP server as `--mcp` in-process (memory pipes instead of stdio, `McpHost.ServeAsync` already serves a stream) and calls the tools through an MCP client. So:
- no second list of commands to maintain: every new tool works in scripts, and `docs/mcp.md` is the reference;
- the same argument checks, file access policy (`FileAccessPolicy`, `--allow`), error messages and history as an agent gets;
- nothing new for Native AOT: the server and `McpJson` already work under AOT.

### Syntax (to refine)

```
# Convert a photo to black and white, for the web.
open_image path=$file
apply_effect effect="Black and White"
resize_image width=1920
save_image path=~/Downloads/web/$name.jpg overwrite=true
close_image
```

- One command per line; `#` starts a comment; blank lines are ignored.
- Parameters are `name=value`. Values with spaces are quoted (`"…"`). Numbers, `true`/`false`, and lists (`values=[1,2,3]`) are converted to the JSON the tool expects, using the tool's input schema (so `width=1920` is a number, `path=1920` stays a string).
- Command names are case-insensitive (`OPEN_IMAGE` works).
- `~` is the home folder.
- Variables, for `--input`: `$file` (full path), `$name` (file name without extension), `$dir` (its folder), `$ext`.
- Without `--input`, the script runs once. With it, it runs once per input file, in a fresh workspace each time (documents left open are closed without saving).

### Behaviour

- Stops at the first failing command: prints `script.txt:3: apply_effect: Unknown effect 'Black & White'.` to stderr and exits with code 1. With several inputs, stops at the first failing file; `--keep-going` runs them all and reports the failures at the end.
- Prints one line per command to stderr (`[2/4] apply_effect effect=Black and White … ok, 120 ms`); `--quiet` only prints errors.
- Exit codes: 0 all good, 1 a command failed, 2 bad script or arguments (unknown command or parameter, caught before anything runs).
- Allowed folders: as `--mcp` — `--allow`, `CINNABARSHARP_MCP_ALLOW`, else the current folder. The folders of the `--input` files are allowed too (the user named them); the policy doesn't tell reading from writing. Writing outside the allowed folders fails, like for an agent.

## Tasks (all done)

- [x] **Diagnostic log first** (`CINNABARSHARP_LOG=1`, Desktop): today only crashes reach the terminal; errors shown in a dialog, exceptions in fire-and-forget tasks (comic and TV previews, `_ = …Async()`) and Avalonia's own warnings (bindings, rendering, sent to `Trace` by `LogToTrace`) are invisible — so a feature broken by Native AOT (a missing JSON type, a trimmed member) can fail silently. Write, to the log file always and also to stderr when the variable is set:
  - every error shown to the user (`IDialogService.ShowErrorAsync`: title and message, plus the exception when there is one);
  - `TaskScheduler.UnobservedTaskException` and `AppDomain.UnhandledException`;
  - Avalonia's log at Warning and above (`LogToTrace` replaced by a sink that writes to stderr when the variable is set);
  - same format as `StartupTrace` (`log <time> <level> <source>: <message>`), and keep `StartupTrace` as is.
  - **always** in `log.txt` in the app-data folder (`~/Library/Application Support/CinnabarSharp`, `~/.local/share/CinnabarSharp`, `%LOCALAPPDATA%\CinnabarSharp`), with the version and OS on the first line of each session; rotated at 1 MB (keeps `log.1.txt`); never throws if the folder can't be written;
  - "Open Log Folder" menu item: Help on Windows/Linux, the application menu on macOS (no Help menu there), opening the folder in the file manager;
  - `--run` and `--mcp` log to stderr only (no window, and stdout is the protocol for `--mcp`).
  Test: a headless UI test (temp log folder, like `RecentFilesStore` in `TestHarness`) triggers an error dialog and an exception in a background task and checks both lines are in the file; rotation tested in isolation.
- [x] **Parser** (`CinnabarSharp.Mcp/Scripting/ScriptParser.cs`, pure, no MCP): lines → `(line number, command, raw parameters)`; quotes, comments, variables, `~`. Unit tests for the syntax and its errors.
- [x] **Argument conversion** from the tool's input schema (`ListTools` gives each tool's JSON schema): string / number / integer / boolean / array; unknown command or parameter is a script error with the line number. Checked for the whole script before running it.
- [x] **Runner** (`ScriptRunner`): starts the server in-process over pipes with the headless services (as `RunAsync` does), an MCP client, calls each command, reports, exit code. Loop over `--input` (globs expanded by us, since Windows' shell doesn't); `--keep-going`.
- [x] **Entry point**: `Program.Main` branches on `--run` before Avalonia starts, like `--mcp`.
- [x] **Tests** (Mcp.Tests): run scripts through the real executable (`McpTestServer.Command`, so they also run against a native build with `CINNABARSHARP_TEST_EXE`): a script that opens `sample1.heic`, applies effects, saves PNG/JPEG/ORA and reopens them; error cases (bad command, bad parameter, missing file, write outside the allowed folders) with the expected line and exit code; several inputs.
- [x] **Release smoke test**: `packaging/smoke-test.txt` calls **every MCP tool at least once** on the sample images (open HEIC and PNG, every effect from `list_effects`, layers, selections, resize/crop/rotate, speech bubble excluded in headless mode, TV and comic page, save/export in PNG, JPEG and ORA, then reopen them). A test in Mcp.Tests fails when a tool is missing from the script, so new tools get covered. A step in `release.yml` after `Package` runs the packaged binary (`.app/Contents/MacOS/CinnabarSharp`, `CinnabarSharp.exe`, `CinnabarSharp`) with `--run` on it and fails the job, so no release with a broken native build.
- [x] **Docs**: `docs/automation.md` (syntax, variables, examples), a line in the README, and the AOT rule in CLAUDE.md stays (new tools are scriptable automatically).

## Decisions (2026-10-04)

1. **Syntax**: MCP tool names only, case-insensitive. No aliases.
2. **Several inputs**: stop at the first failing file by default; `--keep-going` runs every file and reports the failures at the end (exit code 1 if any failed).
3. **No assertions in scripts.** The first goal is to exercise **every feature** with the released binaries: the release smoke test calls every MCP tool at least once and passes if they all succeed.
4. **In the app** (File › Run Script…): later, not in this plan.
5. **Writing**: the allowed folders, as for MCP; the current folder by default.
6. **Log file**: yes. Always on, small and rotating, in the app-data folder, with an "Open Log Folder" menu item. `CINNABARSHARP_LOG=1` also copies it to stderr.
