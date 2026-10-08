# Usage statistics

CinnabarSharp can send anonymous usage statistics, so that we know how many people use it, on which systems, and which
features matter. **It is off unless you agree**, and builds without a statistics endpoint never ask and never send.

## What you see

- The first time the app starts (after the update question), it asks *Help improve CinnabarSharp?*. Nothing is counted
  or sent before you answer, and a "no" is remembered.
- Help › **Send Anonymous Usage Statistics** (in the application menu on macOS) turns it on or off at any time. Turning it
  off forgets the random id and anything not sent yet; turning it on again starts with a new id.
- `CINNABARSHARP_TELEMETRY=0` in the environment turns it off whatever the setting says.
- The command line modes (`--mcp`, `--run`) never send statistics.

## What is sent

Counts, by event name, in one HTTPS `POST` when the app starts, every 15 minutes while it runs (only if something was
counted), and when it quits. Example:

```json
{
  "schema": 1,
  "installId": "3f6c0e2a9b5d4c71a8e2f0b4d6c8a1e3",
  "sessionId": "b1d2c3e4f5a60718293a4b5c6d7e8f90",
  "appVersion": "0.9.6",
  "os": "macos",
  "osVersion": "15.2",
  "arch": "arm64",
  "locale": "fr-FR",
  "from": "2026-10-08T10:00:00Z",
  "to": "2026-10-08T10:15:00Z",
  "events": {
    "app_start": 1,
    "tool:Paintbrush": 12,
    "menu:Edit/Copy": 3,
    "menu:Effects/Blurs/Gaussian Blur": 1,
    "effect:Gaussian Blur": 1,
    "open:jpg": 2,
    "save:ora": 1
  }
}
```

| Field | Meaning |
|---|---|
| `installId` | Random id made when you agree, kept in the settings file; not derived from your machine or account |
| `sessionId` | Random id of this run of the app |
| `appVersion`, `os`, `osVersion`, `arch`, `locale` | The app version, the system (major.minor version), the processor and the UI language |
| `from`, `to` | The period the counts cover |
| `events` | How many times each thing was used in that period |

Event names:

| Name | Counted when |
|---|---|
| `app_start` | The app starts |
| `tool:<name>`, `vector_tool:<name>` | A tool is picked (image or drawing tools) |
| `menu:<menu path>` | A menu command runs, from the menu or its shortcut |
| `effect:<name>` | An adjustment or effect is applied (not when its dialog is cancelled) |
| `open:<format>`, `save:<format>` | A file of that format is opened or saved |

**Never sent:** pictures or any pixel, file names or paths (recent files are not counted), text you type, layer names,
your IP address in the data (the receiving server sees it, like any web request; see below), anything about your accounts.

Names are built only from the app's own labels, and the client refuses any name that doesn't fit
`kind` or `kind:detail` with letters, digits, spaces and `. _ - / &` (so an absolute path can't pass), at most 80
characters and 500 different names per batch. A batch that could not be sent is kept in memory for the next one and
lost when the app quits.

## For maintainers: choosing where it goes

The endpoint is set at build time; without one nothing is sent and the question is never shown.

- Release builds: set the repository variable **`TELEMETRY_ENDPOINT`** (GitHub › Settings › Secrets and variables ›
  Actions › Variables). `release.yml` passes it to `packaging/package.sh` as `CINNABARSHARP_TELEMETRY_URL`, which builds
  it in with `-p:TelemetryEndpoint=…` (an `AssemblyMetadata` attribute read by `TelemetryEndpoint`).
- Local test: `CINNABARSHARP_TELEMETRY_URL=http://localhost:8787/ dotnet run --project CinnabarSharp.Desktop` (HTTPS
  anywhere, plain HTTP only to this machine).

The endpoint receives the JSON above and answers any 2xx to accept it; any other answer, or no answer within 10 seconds,
keeps the batch for later. The request has `User-Agent: CinnabarSharp/<version>` and nothing else identifying.

Put a small relay of your own in front of the analytics service (for example a Cloudflare Worker), rather than an
analytics service's own URL, so the provider can change without a new release and the service's key never ships in
the app. The relay should:

- check the body: `schema` is known, `appVersion` is a published release, `os` is one of `macos`, `windows`, `linux`,
  event names match the pattern above, counts are small positive integers, `from`/`to` are recent;
- limit requests per IP address and per `installId` (for example 20 per hour), and reject bodies over a few kilobytes;
- not store the IP address, only use it for the limits;
- forward to the analytics service (Aptabase, PostHog, a database…), mapping `events` to the service's events.

Anyone can send made-up batches, since the app is open source: count users as `installId`s seen on at least two
different days, and compare with the release download counts.

Code: `TelemetryClient` (Core, counting, batching, sending), `TelemetryEndpoint` (Desktop, build-time endpoint),
`MainViewModel.Telemetry.cs` (consent and the menu switch), `MainWindow.Tracked` (menu commands).
