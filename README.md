# CvWeb

CvWeb is a two-tier portfolio application built with .NET 10.

- Tier 1: a CV-first Blazor WebAssembly landing page
- Tier 2: a live technical dashboard backed by synthetic telemetry, MJPEG, and WebRTC diagnostics

## Architecture

- Frontend SPA: `src/CvWeb.Client`
- Backend Data Pump API: `src/CvWeb.DataPump`
- Solution entry: `CvWeb.slnx`

## Features

### CV and Portfolio Landing (`/`)

- Cinematic visual direction and motion system
- Structured CV content with career outcomes and technical toolkit
- Responsive hero and profile snapshot layout
- Navigation to live portfolio dashboard

### Live Dashboard (`/dashboard` and `/control-room`)

- Project 01: WASM image drift comparison
- Project 02: SignalR telemetry sparkline and health chips
- Project 03: MJPEG boundary decoder on canvas
- Project 04: Browser-local alarm triage model
- Project 05: WebRTC diagnostics and frame health

Each widget is wrapped in a flip-card that shows challenge, solution, stack, and source link.

### Data Pump API

Core endpoints:

- `GET /api/health`
- `GET /api/telemetry?node=edge-gateway-a&samples=24`
- `GET /api/video/frame-meta?width=1920&height=1080&fps=30`
- `GET /api/webrtc/tracks?profile=balanced`
- `GET /api/mjpeg/stream?fps=30`
- SignalR hub: `/hubs/telemetry`

## Local Development

From repository root:

```bash
dotnet restore CvWeb.slnx --configfile NuGet.Config
dotnet build CvWeb.slnx --configuration Release
dotnet run --project src/CvWeb.DataPump
dotnet run --project src/CvWeb.Client
```

Default local addresses:

- Client: `http://localhost:5205`
- Data Pump: `http://localhost:5094`

## CI/CD

- CI workflow: `.github/workflows/ci.yml`
- GitHub Pages deploy workflow: `.github/workflows/deploy-client-pages.yml`

The deploy workflow:

1. Publishes Blazor static assets
2. Rewrites base href to `/cvweb/`
3. Applies `DATAPUMP_BASE_URL` when configured
4. Creates `404.html` SPA fallback
5. Deploys to GitHub Pages

## Hosting

Published frontend target:

- `https://cjhawes.github.io/cvweb/`

Backend can be hosted on any ASP.NET Core-capable service.

## Documentation

- Developer handbook: `docs/developer-guide.md`
- Hosting runbook: `docs/hosting.md`
