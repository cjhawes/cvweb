# CvWeb

A two-tier Single Page Application that combines a cinematic CV landing page with a live technical sandbox.

## Architecture

- Frontend: Blazor WebAssembly SPA in src/CvWeb.Client
- Backend Data Pump: ASP.NET Core Minimal API in src/CvWeb.DataPump
- Solution: CvWeb.slnx

## Tier 1 Landing Page

The root route / is implemented as a high-impact visual CV with:

- Dark, modern visual design with cyan and electric orange accents
- Bold headline typography
- Scroll reveal animations for content sections
- Subtle parallax and animated particle background
- Terminal-style CTA: > Enter the Control Room

## Tier 2 Data Pump API

The Data Pump provides synthetic streams and metadata for sandbox scenarios.

Core endpoints:

- GET /api/health
- GET /api/telemetry?node=edge-gateway-a&samples=24
- GET /api/video/frame-meta?width=1920&height=1080&fps=30
- GET /api/webrtc/tracks?profile=balanced
- GET /api/mjpeg/stream?fps=30
- SignalR hub: /hubs/telemetry

## Tier 2 Control Room Dashboard

The dashboard route /dashboard hosts five concurrent widgets in a dark CSS Grid control-room layout:

- Widget 1: WASM image byte-level drift detection
- Widget 2: SignalR live telemetry sparkline
- Widget 3: MJPEG boundary decoder rendered to canvas
- Widget 4: Browser-local alarm triage model and priority feed
- Widget 5: WebRTC diagnostics with live frame health metrics

Every widget supports a stage-4 flip interaction with:

- Challenge
- Solution
- Stack
- Direct source code link

## Local Development

From the repository root:

1. Restore dependencies
   dotnet restore CvWeb.slnx --configfile NuGet.Config
2. Build solution
   dotnet build CvWeb.slnx --configuration Release
3. Run backend Data Pump (terminal 1)
   dotnet run --project src/CvWeb.DataPump
4. Run Blazor client (terminal 2)
   dotnet run --project src/CvWeb.Client

## Hosting Model

- SPA hosting target: GitHub Pages or Azure Static Web Apps
- API hosting target: any free ASP.NET-capable platform (for example Azure free tier or Render)
- CI/CD: GitHub Actions workflows in .github/workflows

## Notes For GitHub Pages

This repository is configured for project pages at:

- https://cjhawes.github.io/cvweb/

Setup steps:

1. Open repository settings and set Pages source to GitHub Actions.
2. In repository settings, add variable DATAPUMP_BASE_URL with your deployed Data Pump URL.
3. Push to main, or run the deploy-client-pages workflow manually.
4. Wait for the deploy job to complete and open https://cjhawes.github.io/cvweb/.

Notes:

- The workflow rewrites base href to /cvweb/ during publish.
- A 404.html fallback is generated for SPA route refresh support.
