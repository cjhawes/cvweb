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

- The deploy-client-pages workflow publishes static assets from CvWeb.Client.
- If you deploy to a project pages path, set the base href in src/CvWeb.Client/wwwroot/index.html to /REPO_NAME/.
- For root-domain pages, keep base href as /.
