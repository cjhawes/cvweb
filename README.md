# CvWeb

CvWeb is a Blazor WebAssembly portfolio application that runs entirely in the browser.

The project presents a CV-first landing page and a live engineering dashboard where all telemetry, MJPEG slicing, and diagnostics are generated client-side through a browser Web Worker.

## Architecture

- Frontend SPA: `src/CvWeb.Client`
- Synthetic stream backend: `src/CvWeb.Client/wwwroot/js/mock-stream.worker.js`
- Solution entry: `CvWeb.slnx`

Runtime model:

1. `Dashboard` starts `IMockStreamService`.
2. `MockStreamService` starts the worker via JS interop.
3. Worker emits telemetry, MJPEG byte chunks, WebRTC profile data, and telemetry-grid frames.
4. JS routes high-frequency canvas streams directly to render loops and forwards typed payloads into .NET.
5. Widgets render bounded, local state with explicit disposal on route exit.

## Dashboard Widgets

- Project 01: GPGPU 4K byte-level alignment checker
- Project 02: Worker telemetry sparkline and health chips
- Project 03: Software MJPEG multipart decoder (byte chunk slicing in C#)
- Project 04: Browser-local AI alarm triage (scoring, debounce, token-bucket rate limit)
- Project 05: WebRTC probe (RTC stats mapping, health scoring, rolling charts)
- Project 06: 1,024-sensor telemetry grid at 60Hz with fixed-size ring buffer rendering

Each widget is wrapped by `WidgetCard` (flip-card) metadata for challenge, solution, stack, and source references.

## Local Development

From repository root:

```bash
dotnet restore CvWeb.slnx --configfile NuGet.Config
dotnet build CvWeb.slnx --configuration Release
dotnet test tests/CvWeb.Client.Tests/CvWeb.Client.Tests.csproj --configuration Release
dotnet run --project src/CvWeb.Client
```

Default local address:

- Client: `http://localhost:5205`

## CI/CD

- CI workflow: `.github/workflows/ci.yml`
- GitHub Pages deploy workflow: `.github/workflows/deploy-client-pages.yml`

Deployment workflow steps:

1. Publish Blazor static assets.
2. Rewrite base href to `/cvweb/`.
3. Generate `404.html` SPA fallback.
4. Deploy to GitHub Pages.

Published target:

- `https://cjhawes.github.io/cvweb/`

## Documentation

- System architecture: `docs/ARCHITECTURE.md`
- Developer handbook: `docs/developer-guide.md`
- Hosting notes: `docs/hosting.md`
