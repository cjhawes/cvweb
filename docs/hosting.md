# Hosting Guidance

For full engineering context and operational guidance, see `docs/developer-guide.md`.

## Option A: GitHub Pages + External API Host

### 1. Prepare the backend Data Pump host

1. Deploy src/CvWeb.DataPump to a free ASP.NET Core host (for example Azure App Service free tier or Render).
2. Confirm these endpoints are reachable:
	- /api/health
	- /api/telemetry
	- /hubs/telemetry
	- /api/mjpeg/stream
3. Keep HTTPS enabled for browser mixed-content safety.
4. After deployment is stable, restrict CORS to your production frontend origin.

### 2. Configure the GitHub repository

1. Open repository settings for cjhawes/cvweb.
2. In Settings > Pages, set source to GitHub Actions.
3. In Settings > Secrets and variables > Actions > Variables, add:
	- DATAPUMP_BASE_URL = https://your-datapump-host

### 3. Publish the SPA

1. Push to main or run deploy-client-pages manually.
2. The workflow will:
	- Publish Blazor WASM static assets
	- Rewrite base href to /cvweb/
	- Generate 404.html SPA fallback
	- Deploy to GitHub Pages
3. Open https://cjhawes.github.io/cvweb/.

### 4. Verify production behavior

1. Check /dashboard loads all five widgets.
2. Confirm SignalR telemetry is live.
3. Confirm MJPEG boundary count increments.
4. Confirm WebRTC diagnostics moves to connected state.
5. Confirm browser refresh on /dashboard resolves correctly (SPA fallback).

## Option B: Azure Static Web Apps + External API Host

1. Deploy src/CvWeb.Client to Azure Static Web Apps.
2. Host src/CvWeb.DataPump on an external free tier service.
3. Configure API endpoint and allowed origins for the SPA domain.

## API Deployment Considerations

- Keep health endpoint public for uptime checks.
- Lock CORS to production origins once deployment is stable.
- Add rate limiting if public traffic is expected.
- Use environment variables for API URL and any future signaling configuration.
