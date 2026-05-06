# Hosting Guidance

## Option A: GitHub Pages + External API Host

1. Enable GitHub Pages in repository settings.
2. Use the deploy-client-pages workflow to publish src/CvWeb.Client.
3. Host src/CvWeb.DataPump on a free ASP.NET-capable service.
4. Configure CORS and API base URL for the production origin.

## Option B: Azure Static Web Apps + External API Host

1. Deploy src/CvWeb.Client to Azure Static Web Apps.
2. Host src/CvWeb.DataPump on an external free tier service.
3. Configure API endpoint and allowed origins for the SPA domain.

## API Deployment Considerations

- Keep health endpoint public for uptime checks.
- Lock CORS to production origins once deployment is stable.
- Add rate limiting if public traffic is expected.
- Use environment variables for API URL and any future signaling configuration.
