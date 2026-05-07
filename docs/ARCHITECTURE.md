# CvWeb Architecture

## 1. System Overview

CvWeb is a browser-only Blazor WebAssembly system hosted as static assets.

- UI shell and widgets run in `src/CvWeb.Client`.
- High-frequency synthetic runtime data is generated in `wwwroot/js/mock-stream.worker.js`.
- JS interop orchestration is implemented in `wwwroot/js/site.js`.
- No server-side API or SignalR backend is required at runtime.

## 2. Runtime Topology

### 2.1 Main Components

- `Dashboard` page: starts/stops runtime sessions for the route.
- `IMockStreamService` / `MockStreamService`: C# broker that fans worker payloads to widgets via bounded channels.
- `cvDashboard` JS module: controls Web Worker lifecycle, WebGL, Canvas, and WebRTC probe sessions.
- Widget components: render metrics and visuals; processing-heavy logic stays in services or JS loops.

### 2.2 Worker and Interop Flow

1. `Dashboard` calls `MockStreams.StartAsync()`.
2. `MockStreamService` invokes `cvDashboard.startMockStreamWorker`.
3. Worker emits:
   - telemetry payloads
   - telemetry-grid frames
   - MJPEG multipart byte chunks
   - WebRTC track profile envelopes
4. JS routes telemetry-grid frames directly into canvas session rings.
5. JS forwards other payloads to `.NET` via `HandleWorkerMessage`.
6. `MockStreamService` publishes bounded channel streams for subscribed widgets.

## 3. Active Widget Modules

### 3.1 Project 01: GPGPU Alignment Checker

- Component: `GpuAlignmentChecker`
- Pipeline: WebGL2 fragment compare pass over two static 4K reference textures.
- Output: changed bytes, mismatched pixels, drift percent, elapsed pass time.

### 3.2 Project 02: Telemetry Stream Widget

- Component: `SignalRTelemetryWidget`
- Data source: worker telemetry channel via `IMockStreamService.SubscribeTelemetry`.
- Output: CPU/memory/loss chips and rolling sparkline.

### 3.3 Project 03: MJPEG Software Decoder

- Component: `MjpegDecoder`
- Data source: `MjpegByteChunk` channel.
- Decode strategy: boundary/header/content-length parsing in C# with bounded carry buffer.
- Render strategy: fixed cadence draw loop to canvas via JS frame decode.

### 3.4 Project 04: AI Alarm Triage

- Component: `AlarmTriage`
- Engine: `AlarmTriageEngine`
- Data source: raw telemetry JSON channel.
- Logic: weighted scoring, baseline anomaly boost, cluster grouping, debounce, token-bucket rate limiting.

### 3.5 Project 05: WebRTC Probe

- Component: `WebRtcProbe`
- Engine: `WebRtcProbeMetricsEngine`
- Data source: simulated peer connection stats sampled in JS and pushed to .NET.
- Output: health score and rolling bitrate/loss/jitter chart polylines.

### 3.6 Project 06: Telemetry Grid

- Component: `TelemetryGrid`
- Data source: telemetry-grid worker frames routed in JS.
- Render strategy: fixed-size ring buffer (256 slots), latest-frame draw, stale backlog drop.

## 4. Performance and Memory Strategy

- Bounded channels with `DropOldest` prevent unbounded growth under bursts.
- Telemetry-grid render bypasses per-frame .NET marshalling for 60Hz stability.
- MJPEG parser caps carry buffer growth and bounds queue depth.
- Worker, WebRTC, GPU, canvas, and subscription sessions are disposed on route teardown.
- `MockStreamService.StopAsync` and `DisposeAsync` guarantee worker stop attempt and reference disposal even under JS exceptions.

## 5. UI Composition and DRY Structure

- `WidgetCard` is the shared flip-card shell.
- Dashboard cards are rendered from a metadata list with `DynamicComponent`.
- Shared UI primitives:
  - `WidgetStatus`
  - `MetricChip`
  - `MetricChipRow`
- Widgets retain component-specific rendering while reusing status/chip wrappers.

## 6. Testing Strategy

- Unit tests (`xUnit`) cover service logic:
  - `AlarmTriageEngine`
  - `GpuAlignmentMetrics`
  - `WebRtcProbeMetricsEngine`
  - `MockStreamService`
- Component tests (`bUnit`) cover active widgets and route-facing behavior.

## 7. Phase 6.5 Cleanup Outcomes

### Removed legacy/orphaned artifacts

- Legacy widgets removed:
  - `AiAlarmTriageWidget`
  - `ImageDiffWidget`
  - `MjpegCanvasWidget`
  - `WebRtcDiagnosticsWidget`
- Orphaned image assets removed:
  - `camera-live.svg`
  - `camera-reference.svg`
- Backend scaffold removed:
  - `src/CvWeb.DataPump`
  - solution/workflow references to DataPump deployment wiring

### Contract and interop cleanup

- Removed obsolete `MjpegStreamSample` and `SubscribeMjpeg` stream path.
- Removed legacy JS compatibility APIs not used by active components.

### Documentation and API hygiene

- Public client-side APIs now include XML documentation comments.
- README and architecture docs now describe the implemented browser-only runtime.
