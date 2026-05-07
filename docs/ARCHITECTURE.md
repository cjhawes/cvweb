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

## Phase 2: GPGPU Alignment Checker Widget

### Objective
Implement a byte-level 4K reference texture comparison widget using GPU fragment shading through C# JS interop while preserving the existing dashboard grid and card shell CSS.

### Key Decisions
- Added `GpuAlignmentChecker` as a new dashboard widget component with a `.razor` + `.razor.cs` split to keep UI and interop orchestration cleanly separated.
- Added static 4K reference assets at:
  - `src/CvWeb.Client/wwwroot/images/gpu-reference-a.svg`
  - `src/CvWeb.Client/wwwroot/images/gpu-reference-b.svg`
- Added `GpuAlignmentMetrics` service helper to keep drift/threshold logic deterministic and testable.
- Extended `wwwroot/js/site.js` with:
  - `startGpuAlignmentChecker`
  - `stopGpuAlignmentChecker`
  - disposal integration in `disposeAllDashboard`

### Runtime Flow
1. `GpuAlignmentChecker` mounts and calls `cvDashboard.startGpuAlignmentChecker(...)`.
2. JS creates a WebGL2 context and compiles the compare/preview fragment shader pipeline.
3. Two static 4K textures are loaded, uploaded, and compared channel-by-channel in a GPU render pass.
4. JS performs a single `readPixels` against the 4K mismatch buffer and computes:
   - changed bytes
   - mismatched pixels
   - elapsed pass time
5. Metrics are returned to C# through `[JSInvokable] UpdateGpuAlignmentResult(...)` and rendered in the widget.
6. Widget disposal calls `cvDashboard.stopGpuAlignmentChecker(...)` to release shaders, buffers, textures, and framebuffer state.

### Performance Notes
- GPU resources are created once per widget session and explicitly disposed on teardown.
- Readback uses a reusable typed array buffer to avoid repeated allocations.
- Shader compare uses nearest-neighbor sampling to preserve deterministic byte-level channel comparisons.
- Dashboard card count and grid selectors are unchanged, preserving existing layout behavior.

## Phase 3: Synthetic Telemetry Grid Canvas Widget

### Objective
Implement a high-frequency telemetry engine widget that simulates 1,024 IoT sensors at 60Hz and renders directly to HTML5 Canvas without disrupting existing dashboard layout or card styling.

### Key Decisions
- Added `TelemetryGrid` widget as a `.razor` + `.razor.cs` pair to keep presentation and JS interop orchestration separated.
- Extended `wwwroot/js/mock-stream.worker.js` with a new `telemetry-grid` stream:
  - 32x32 sensor mesh (1,024 sensors)
  - 60Hz frame cadence
  - per-frame intensity, alert mask, and aggregate metrics
- Extended `wwwroot/js/site.js` with:
  - `startTelemetryGrid(canvasId, dotNetRef, gridWidth, gridHeight)`
  - `stopTelemetryGrid(canvasId)`
  - telemetry-grid routing from worker to renderer sessions
  - disposal integration in `disposeAllDashboard`
- Added additive CSS in `app.css` via `.telemetry-grid-canvas` without changing existing grid selectors.

### Circular Buffer Strategy
- Each telemetry-grid session allocates a fixed ring buffer with 256 slots.
- Each slot pre-allocates:
  - `Uint8Array(1024)` for intensity values
  - `Uint8Array(1024)` for alert flags
- Write pointer advances via power-of-two masking (`index & 255`) for constant-time wrap-around.
- When producers outrun rendering, the oldest slot is overwritten and a dropped-frame counter is incremented.
- Renderer always consumes the newest slot and discards stale backlog to protect frame pacing.

### Runtime Flow
1. Dashboard starts `MockStreamService`, which starts the worker.
2. Worker emits `telemetry-grid` frames at 60Hz.
3. `site.js` routes `telemetry-grid` payloads directly into active telemetry-grid session rings.
4. `requestAnimationFrame` loop paints the latest frame to Canvas using precomputed color palette and single `putImageData` + `drawImage` pass.
5. Widget receives 1Hz summary stats via `[JSInvokable] UpdateTelemetryGridStats(...)` for status chips.

### Performance Notes
- High-frequency telemetry-grid frames bypass per-frame `.NET` deserialization to reduce interop overhead.
- Circular buffer memory is bounded and pre-allocated to avoid allocation churn under sustained 60Hz load.
- Rendering uses Canvas pixel buffers instead of DOM node updates, keeping UI thread work predictable.
- Existing dashboard routing and CSS grid behavior are preserved.

## Phase 4: Software MJPEG Decoder Widget

### Objective
Implement a browser-local MJPEG decoder that ingests raw multipart HTTP byte chunks from the worker, slices frame boundaries in Blazor, and paints decoded frames to Canvas at 30fps without using video tags.

### Key Decisions
- Added `MjpegDecoder` widget as a `.razor` + `.razor.cs` pair to separate UI shell from byte-stream decode orchestration.
- Replaced dashboard Project 03 rendering component with `MjpegDecoder` while preserving card layout and flip-card metadata flow.
- Extended `IMockStreamService` and `MockStreamService` with `MjpegByteChunk` transport and subscription APIs.
- Extended worker stream simulation (`mock-stream.worker.js`) to emit fragmented multipart MJPEG byte chunks (`mjpeg-byte-chunk`) with sequence/timestamp metadata.

### Byte-Slicing Strategy
- Worker emits multipart payload segments with structure:
  - boundary line (`--frame`)
  - headers (`Content-Type`, `Content-Length`)
  - header terminator (`\r\n\r\n`)
  - synthetic JPEG bytes
  - trailing CRLF
- Decoder keeps a carry buffer for partial chunks and appends each incoming chunk.
- Boundary parser scans byte spans for:
  1. boundary marker
  2. header terminator
  3. content-length value
  4. full frame payload availability
- Complete frame payloads are enqueued; residual tail bytes are retained in carry for the next chunk.

### Synchronization Strategy
- Ingest loop: consumes chunk channel continuously and extracts complete frames as they become available.
- Render loop: fixed cadence (~30fps) to draw one latest frame to canvas.
- Frame queue is bounded (max depth 4) to prevent UI stall under bursty chunk ingress.
- When queue overflows, stale frames are dropped (drop-oldest) and widget status degrades until queue pressure stabilizes.

### Runtime Flow
1. Dashboard starts `MockStreamService` and worker.
2. Worker emits `mjpeg-byte-chunk` envelopes with base64-encoded byte slices.
3. `MockStreamService` decodes base64 and fans out `MjpegByteChunk` objects over bounded channels.
4. `MjpegDecoder` parses multipart boundaries in C# and enqueues full frame payloads.
5. Render loop calls JS interop draw routine to decode frame bytes and paint onto canvas.

### Performance Notes
- Byte chunk fanout uses bounded channels with `DropOldest` backpressure semantics.
- Decoder parsing is span-oriented with carry buffer reuse to avoid unbounded stream growth.
- Rendering is canvas-based and avoids DOM node churn or native `<video>` playback overhead.
