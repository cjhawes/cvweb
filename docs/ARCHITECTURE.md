# CvWeb Architecture

## Phase 1: MockStreamService Web Worker Data Pump

### Objective
Replace dashboard runtime dependencies on external API/SignalR endpoints with an in-browser, serverless data pump while preserving existing routing and UI layout.

### Key Decisions
- Introduced `IMockStreamService` and `MockStreamService` in `CvWeb.Client`.
- Implemented bounded channel fan-out using `System.Threading.Channels` for:
  - Telemetry stream
  - MJPEG boundary/fps stream
  - WebRTC profile metadata cache
- Added dedicated client worker script `wwwroot/js/mock-stream.worker.js`.
- Added JS bridge APIs in `wwwroot/js/site.js` to start/stop worker and forward worker payloads into C# service.

### Runtime Flow
1. Dashboard route initializes and calls `MockStreamService.StartAsync()`.
2. `MockStreamService` starts `mock-stream.worker.js` through JS interop.
3. Worker emits telemetry, MJPEG, and WebRTC profile payloads.
4. JS bridge forwards payloads to C# via `HandleWorkerMessage`.
5. C# service deserializes and writes to bounded channels.
6. Widgets subscribe to channel readers and render updates.

### Widget Integration
- `SignalRTelemetryWidget` now consumes telemetry channel data.
- `AiAlarmTriageWidget` now consumes telemetry channel data for score/priority generation.
- `MjpegCanvasWidget` now consumes MJPEG channel data and updates decoder boundary state through JS interop.
- `WebRtcDiagnosticsWidget` now uses `IMockStreamService` for track profile metadata.

### Deployment Path Safety
- Worker URL is resolved relative to `document.baseURI`, ensuring compatibility with `/cvweb/` base href on GitHub Pages.

### Performance Notes
- Bounded channels use `DropOldest` to prevent unbounded queue growth under bursty conditions.
- Stream fan-out is non-blocking (`TryWrite`) to avoid producer stalls.
- Worker message flow removes dashboard hard dependency on remote network endpoints for core telemetry simulation.
