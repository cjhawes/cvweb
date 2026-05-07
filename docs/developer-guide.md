# CvWeb Developer Guide

This guide is the primary technical handbook for developing, maintaining, and deploying CvWeb.

## 1. Purpose and Scope

CvWeb is a two-tier portfolio system:

- Tier 1: A Blazor WebAssembly CV and portfolio landing experience.
- Tier 2: A live technical dashboard driven by synthetic backend streams.

This document covers:

- Architecture and repository structure
- Frontend and backend implementation details
- Widget development workflow
- Configuration and environment management
- Build, validation, CI/CD, and release practices
- Deployment and operations guidance

## 2. Architecture Overview

### 2.1 Logical Architecture

- Frontend SPA: `src/CvWeb.Client`
- Backend API + SignalR hub: `src/CvWeb.DataPump`
- Shared orchestration: `CvWeb.slnx`

High-level request/stream flow:

1. Browser loads Blazor WebAssembly assets from static hosting.
2. Home page initializes animation and visual effects via JS interop.
3. Dashboard widgets pull synthetic metadata over HTTP and subscribe to live telemetry over SignalR.
4. Backend continuously emits telemetry signals and synthetic media diagnostics.

### 2.2 Runtime Integration Points

- HTTP endpoints (backend):
  - `/api/health`
  - `/api/telemetry`
  - `/api/video/frame-meta`
  - `/api/webrtc/tracks`
  - `/api/mjpeg/stream`
- SignalR hub:
  - `/hubs/telemetry`
- Client configuration:
  - `src/CvWeb.Client/wwwroot/appsettings.json` -> `DataPumpBaseUrl`

## 3. Repository Layout

```
CvWeb.slnx
NuGet.Config
README.md
docs/
  developer-guide.md
  hosting.md
src/
  CvWeb.Client/
    App.razor
    Program.cs
    Pages/
      Home.razor
      Dashboard.razor
      NotFound.razor
    Components/
      WidgetCard.razor
      Widgets/
    wwwroot/
      css/app.css
      js/site.js
      appsettings.json
  CvWeb.DataPump/
    Program.cs
.github/workflows/
  ci.yml
  deploy-client-pages.yml
```

## 4. Local Development Setup

## 4.1 Prerequisites

- .NET SDK 10.x
- Git
- Optional: GitHub CLI for CI and release workflows

## 4.2 Restore and Build

From repository root:

```bash
dotnet restore CvWeb.slnx --configfile NuGet.Config
dotnet build CvWeb.slnx --configuration Release
```

## 4.3 Run the Services

Terminal 1 (backend):

```bash
dotnet run --project src/CvWeb.DataPump
```

Terminal 2 (frontend):

```bash
dotnet run --project src/CvWeb.Client
```

Default local addresses:

- Client: `http://localhost:5205`
- Data Pump: `http://localhost:5094`

## 4.4 Client-to-Backend Target

The client uses `DataPumpBaseUrl` in `src/CvWeb.Client/wwwroot/appsettings.json`.

When running locally, default is:

```json
{
  "DataPumpBaseUrl": "http://localhost:5094"
}
```

## 4.5 GitHub Codespaces Setup and Maintenance

Use this workflow when developing CvWeb in GitHub Codespaces.

Initial setup:

1. Open the repository in GitHub and create a new Codespace from `main`.
2. Wait for the container to finish initialization.
3. In the Codespace terminal, run:

```bash
dotnet restore CvWeb.slnx --configfile NuGet.Config
dotnet build CvWeb.slnx --configuration Release
```

Running the app in Codespaces:

1. Start backend in terminal A:

```bash
dotnet run --project src/CvWeb.DataPump
```

2. Start frontend in terminal B:

```bash
dotnet run --project src/CvWeb.Client
```

3. In the `Ports` view, ensure ports `5094` and `5205` are forwarded.
4. Set port visibility according to your need:
   - `Private` for normal development
   - `Public` only when external device/browser validation is required

Codespaces maintenance checklist:

1. Rebuild after dependency or workflow changes:

```bash
dotnet restore CvWeb.slnx --configfile NuGet.Config
dotnet build CvWeb.slnx --configuration Release
```

2. Keep local branch synchronized with `main` before larger edits.
3. Stop long-running terminals when not actively testing to reduce resource usage.
4. Periodically clean stale artifacts if disk pressure appears:

```bash
dotnet clean CvWeb.slnx
```

5. Before commit/push from Codespaces, run the validation checklist in section 8.

## 5. Frontend Implementation Guide

## 5.1 Bootstrapping

`src/CvWeb.Client/Program.cs` configures:

- Blazor root component mounting
- `HeadOutlet` support
- Scoped `HttpClient` bound to client base URL

## 5.2 Routing and Page Composition

`src/CvWeb.Client/App.razor` routes to pages and applies `MainLayout`.

Primary pages:

- `Home.razor` (`/`): CV-first experience and portfolio framing
- `Dashboard.razor` (`/dashboard` and `/control-room`): live technical widgets
- `NotFound.razor`: fallback route

## 5.3 UI Structure and Styling

Global style system is centralized in `src/CvWeb.Client/wwwroot/css/app.css`:

- Color tokens and layout variables in `:root`
- Hero and content sections for the landing page
- Widget card and dashboard layout rules
- Responsive breakpoints
- Loading and error UI styles

Important responsive behavior:

- Hero uses two-column layout on large screens
- Hero transitions to single-column at medium sizes to prevent text/card collisions
- Widget grid collapses to single-column on narrower screens

## 5.4 JS Interop Surface

`src/CvWeb.Client/wwwroot/js/site.js` exposes two namespaces:

- `window.cvLanding`
  - `init()`
  - `dispose()`
- `window.cvDashboard`
  - `startMjpegDecoder(canvasId, streamUrl, dotNetRef)`
  - `stopMjpegDecoder(canvasId)`
  - `startTelemetryGrid(canvasId, dotNetRef, gridWidth, gridHeight)`
  - `stopTelemetryGrid(canvasId)`
  - `startWebRtcDiagnostics(videoId, dotNetRef)`
  - `stopWebRtcDiagnostics(videoId)`
  - `disposeAll()`

Frontend pages/components call these methods via `IJSRuntime` and clear resources in `IAsyncDisposable` to avoid browser leaks.

## 5.5 Widget Pattern

Widgets are mounted inside `WidgetCard.razor` which provides:

- Front face: live widget content
- Back face: challenge, solution, stack, and source link
- Card flip state and interaction

Current widget implementations:

- `ImageDiffWidget.razor`: local byte-level drift comparison
- `SignalRTelemetryWidget.razor`: streaming sparkline and metrics
- `TelemetryGrid.razor`: 1,024-sensor synthetic mesh rendered at 60Hz on canvas
- `MjpegCanvasWidget.razor`: synthetic multipart stream diagnostics
- `AiAlarmTriageWidget.razor`: local alert scoring model
- `WebRtcDiagnosticsWidget.razor`: synthetic WebRTC connection and health stats

## 5.6 Adding a New Widget

Recommended workflow:

1. Create component in `src/CvWeb.Client/Components/Widgets/`.
2. Keep render and state logic self-contained.
3. If JS interop is needed, add a focused function to `site.js` and a matching stop/dispose path.
4. Add the widget to `Dashboard.razor` inside a `WidgetCard`.
5. Populate challenge/solution/stack/source metadata.
6. Add responsive style rules in `app.css` only if required.
7. Validate on desktop and mobile breakpoints.

## 6. Backend Implementation Guide

## 6.1 Service Setup

`src/CvWeb.DataPump/Program.cs` configures:

- CORS policy (`OpenClient`)
- SignalR hub registration
- Background telemetry broadcast service
- Minimal API endpoints

## 6.2 Data Contracts

Primary contracts include:

- `ServiceStatus`
- `TelemetryEnvelope`
- `TelemetryPoint`
- `TelemetrySignal`
- `VideoFrameMeta`
- `WebRtcTrackEnvelope`
- `WebRtcTrack`

Contracts are kept in the same file as the minimal API for simplicity in this portfolio project.

## 6.3 Telemetry Generation

Telemetry behavior combines:

- Deterministic seeded randomness per node/time bucket for endpoint reads
- Continuous random jitter for live SignalR stream
- Bounded values using `Math.Clamp` for predictable UI rendering

## 6.4 Streaming Paths

- SignalR push: `TelemetryBroadcastService` -> hub method name `telemetry`
- MJPEG endpoint: synthetic multipart stream with `--frame` boundaries and metadata headers

## 6.5 CORS and Production Hardening

Current implementation allows all origins for development convenience.

For production:

1. Restrict allowed origins to known frontend hosts.
2. Restrict methods and headers if possible.
3. Consider rate limits and request throttling.

## 7. Configuration and Environment

## 7.1 NuGet Source Control

`NuGet.Config` is committed to ensure restore consistency and avoid unauthorized private feed failures.

## 7.2 Environment-specific Values

- Local client config: `src/CvWeb.Client/wwwroot/appsettings.json`
- GitHub Pages deployment override: repository variable `DATAPUMP_BASE_URL`

## 8. Build, Validation, and Quality Gates

## 8.1 Standard Validation Steps

Run before commit:

```bash
dotnet restore CvWeb.slnx --configfile NuGet.Config
dotnet build CvWeb.slnx --configuration Release
```

## 8.2 UI Validation Checklist

- Landing page typography does not overlap adjacent cards
- Home and dashboard render on desktop and mobile widths
- Dashboard widgets update without console errors
- Route refresh works on hosted SPA (`404.html` fallback)

## 8.3 Code Hygiene Expectations

- Remove unused templates, assets, and routes
- Keep JS interop functions paired with explicit teardown
- Prefer small focused components and predictable state
- Keep comments brief and only where code intent is non-obvious

## 9. CI/CD Workflows

## 9.1 Continuous Integration

`.github/workflows/ci.yml`:

- Triggers on push to `main` and pull requests
- Restores solution with repository `NuGet.Config`
- Builds in Release mode

## 9.2 GitHub Pages Deployment

`.github/workflows/deploy-client-pages.yml`:

1. Restores and publishes the client
2. Rewrites `<base href>` to `/cvweb/`
3. Optionally rewrites `DataPumpBaseUrl` from `DATAPUMP_BASE_URL`
4. Creates SPA fallback (`404.html`)
5. Uploads `release/wwwroot` artifact and deploys to Pages

## 10. Deployment Playbook

## 10.1 Frontend (GitHub Pages)

1. Set Pages source to GitHub Actions.
2. Configure `DATAPUMP_BASE_URL` repository variable.
3. Push to `main` or run deploy workflow manually.
4. Verify deployment at `https://cjhawes.github.io/cvweb/`.

## 10.2 Backend (External ASP.NET Host)

Deploy `src/CvWeb.DataPump` to an ASP.NET-capable host.

Post-deploy checks:

- `/api/health` reachable over HTTPS
- `/hubs/telemetry` accepts WebSocket/SignalR connections
- `/api/mjpeg/stream` returns multipart stream
- CORS restricted to expected frontend origin(s)

## 11. Operations and Maintenance

## 11.1 Routine Maintenance

- Keep .NET SDK and package versions current
- Monitor GitHub Actions failures and restore errors
- Recheck route/base href behavior after deployment workflow changes
- Periodically validate JS interop teardown behavior for memory leaks

## 11.2 Troubleshooting

Port already in use:

- Stop existing process using the launch port before rerunning `dotnet run`.

SignalR not connecting:

- Confirm `DataPumpBaseUrl`
- Verify CORS on backend host
- Check that `/hubs/telemetry` is publicly reachable

Dashboard widgets stale:

- Confirm backend is online
- Verify browser console for interop failures
- Confirm stream endpoints return data in expected formats

GitHub Pages route refresh 404:

- Verify deployment contains `404.html` copied from `index.html`
- Confirm `<base href="/cvweb/">` rewrite is present in deployed index

## 12. Release Checklist

Before tagging a release:

1. Run full restore and Release build.
2. Validate home and dashboard routes locally.
3. Validate major widget interactions.
4. Confirm docs are aligned with current routes and deployment behavior.
5. Confirm CI and deploy workflows pass on `main`.

## 13. Recommended Next Improvements

- Add automated tests for telemetry shaping logic.
- Add endpoint contract tests for Data Pump responses.
- Add a health dashboard for deployment status and signal quality.
- Split backend records/services into separate files if project scope grows.
- Restrict CORS and add lightweight rate limiting for internet exposure.
