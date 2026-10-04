# Automation scripts

`CinnabarSharp --run script.txt` runs a text file of image operations without a window: on one file, on several files
(batch), or on none (a script that creates images). The commands are the [MCP tools](mcp.md): everything an AI agent can
do, a script can do, with the same checks and the same undo history.

```bash
CinnabarSharp --run to-web.txt --input ~/Photos/*.heic --var out=~/Pictures/web
```

## A script

```
# Black and white, 1920 pixels wide, as JPEG.
open_image path=$file
apply_effect effect="Black and White"
resize_image width=1920
save_image path=$out/$name.jpg format=jpeg jpegQuality=85 overwrite=true
close_image
```

- One command per line: a tool name, then `name=value` arguments. `#` starts a comment; blank lines are ignored.
- Command and argument names are the MCP tools' (`list_effects` gives the effect names; [mcp.md](mcp.md) the rest).
  Command names ignore case.
- A value with spaces is quoted: `effect="Black and White"`. `\"` is a quote inside `"…"`; `'…'` is taken as is.
- Lists and objects are JSON: `values=[10,20,0]`, `parameters={"Radius":6}`, `documents=["a.png","b.png"]`.
- Numbers, `true`/`false` and text are converted from what the tool expects (`width=1920` is a number).
- `~` at the start of a value is your home folder.
- The current image is the one most recently opened, as in MCP: `document=` is only needed to pick another one.

### Variables

`$name` or `${name}` in a value; `$$` is a dollar sign. An unknown variable is an error.

| Variable | Value |
|---|---|
| `$file`, `$dir`, `$name`, `$ext` | the input file being processed: full path, folder, name without extension, extension |
| any `--var name=value` | the value you gave |

## Options

| Option | Meaning |
|---|---|
| `--run script.txt` | the script to run |
| `--input file-or-pattern` | run the script once per file, in order (`*` and `?` in the file name; repeat the option for more). Without it the script runs once. Each file starts from an empty workspace |
| `--var name=value` | a variable for the script (repeatable) |
| `--allow folder` | a folder the script may read and write (repeatable). Default: the current folder, and `CINNABARSHARP_MCP_ALLOW`. The folders of the `--input` files are allowed too |
| `--keep-going` | after a file fails, go on with the next ones and report at the end (default: stop at the first failure) |
| `--quiet` | only print errors |
| `--verbose` | print what each tool returns (on stdout) |

Files outside the allowed folders can't be read or written, replacing a file needs `overwrite=true`, and closing unsaved
work needs `discardChanges=true`, exactly as for an agent.

## Result

Progress goes to stderr, one line per command. The exit code is:

| Code | Meaning |
|---|---|
| 0 | everything worked |
| 1 | a command failed: `script.txt:3: apply_effect: Unknown effect 'Nope'…` |
| 2 | the script or the command line is wrong (unknown command or parameter, missing parameter, wrong type, unclosed quote, unknown option). Nothing ran |

The whole script is checked before anything runs, so a typo on line 40 doesn't leave a batch half done.

## Testing a build

`packaging/smoke-test.txt` calls every MCP tool and every effect, saves in every format and reopens the files. The release
workflow runs it on the **packaged app** of each OS (`packaging/smoke-test.sh`), so a build that can't open, edit or save an
image never reaches users. `Mcp.Tests` fails when a new tool or effect isn't in that script. To run it by hand:

```bash
packaging/smoke-test.sh osx-arm64 0.8.1 artifacts      # after packaging/package.sh osx-arm64 0.8.1 artifacts
```

## Not here (yet)

- Dialogs, menus and the canvas: scripts are headless, like `--mcp`. `add_speech_bubble` needs the app's fonts, so it only
  works when an agent drives the running app.
- Running a script from the app (File › Run Script…).
