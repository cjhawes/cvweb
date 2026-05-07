const state = {
    running: false,
    telemetryTimer: null,
    mjpegTimer: null,
    boundaryCount: 0,
    node: "edge-gateway-a"
};

function clamp(value, min, max) {
    return Math.max(min, Math.min(max, value));
}

function round(value, digits) {
    const factor = Math.pow(10, digits);
    return Math.round(value * factor) / factor;
}

function postMessageEnvelope(type, payload) {
    self.postMessage({ type, payload });
}

function buildTelemetry(nowMs) {
    const phase = nowMs / 1000 / 9;
    const jitter = (Math.random() - 0.5) * 8;

    const cpu = clamp(52 + Math.sin(phase) * 18 + jitter, 18, 99);
    const memory = clamp(58 + Math.cos(phase / 1.8) * 14 + jitter, 22, 97);
    const packetLoss = clamp(0.04 + Math.abs(Math.sin(phase / 1.9)) * 0.54 + Math.random() * 0.08, 0.01, 2.5);
    const activeStreams = 18 + Math.round(Math.abs(Math.cos(phase / 2.4)) * 19) + Math.floor(Math.random() * 4);
    const alertLevel = packetLoss > 1.4 ? 2 : packetLoss > 0.75 ? 1 : 0;

    return {
        node: state.node,
        timestamp: new Date(nowMs).toISOString(),
        cpuLoadPercent: round(cpu, 2),
        memoryLoadPercent: round(memory, 2),
        packetLossPercent: round(packetLoss, 3),
        activeStreams,
        alertLevel
    };
}

function buildTrackEnvelope(profile) {
    let tracks;

    if (profile === "low-bandwidth") {
        tracks = [
            {
                id: "video-main",
                kind: "video",
                direction: "sendonly",
                codec: "VP8",
                maxBitrateKbps: 900,
                targetFps: 24,
                purpose: "Main stream for constrained links"
            },
            {
                id: "telemetry-overlay",
                kind: "video",
                direction: "sendonly",
                codec: "VP8",
                maxBitrateKbps: 350,
                targetFps: 12,
                purpose: "Overlay channel for metrics"
            },
            {
                id: "audio-ops",
                kind: "audio",
                direction: "sendrecv",
                codec: "OPUS",
                maxBitrateKbps: 48,
                targetFps: null,
                purpose: "Operations voice coordination"
            }
        ];
    } else if (profile === "high-fidelity") {
        tracks = [
            {
                id: "video-main",
                kind: "video",
                direction: "sendonly",
                codec: "AV1",
                maxBitrateKbps: 4200,
                targetFps: 60,
                purpose: "Primary high-fidelity stream"
            },
            {
                id: "video-multiview",
                kind: "video",
                direction: "sendonly",
                codec: "VP9",
                maxBitrateKbps: 2800,
                targetFps: 45,
                purpose: "Secondary diagnostics camera"
            },
            {
                id: "audio-ops",
                kind: "audio",
                direction: "sendrecv",
                codec: "OPUS",
                maxBitrateKbps: 96,
                targetFps: null,
                purpose: "Operations voice coordination"
            }
        ];
    } else {
        tracks = [
            {
                id: "video-main",
                kind: "video",
                direction: "sendonly",
                codec: "VP9",
                maxBitrateKbps: 2200,
                targetFps: 30,
                purpose: "Primary real-time stream"
            },
            {
                id: "telemetry-overlay",
                kind: "video",
                direction: "sendonly",
                codec: "VP8",
                maxBitrateKbps: 700,
                targetFps: 15,
                purpose: "Telemetry annotation layer"
            },
            {
                id: "audio-ops",
                kind: "audio",
                direction: "sendrecv",
                codec: "OPUS",
                maxBitrateKbps: 64,
                targetFps: null,
                purpose: "Operations voice coordination"
            }
        ];
    }

    return {
        profile,
        tracks
    };
}

function emitProfiles() {
    postMessageEnvelope("webrtc-profile", buildTrackEnvelope("low-bandwidth"));
    postMessageEnvelope("webrtc-profile", buildTrackEnvelope("balanced"));
    postMessageEnvelope("webrtc-profile", buildTrackEnvelope("high-fidelity"));
}

function startWorker() {
    if (state.running) {
        return;
    }

    state.running = true;
    state.boundaryCount = 0;

    emitProfiles();

    state.telemetryTimer = self.setInterval(() => {
        postMessageEnvelope("telemetry", buildTelemetry(Date.now()));
    }, 300);

    state.mjpegTimer = self.setInterval(() => {
        state.boundaryCount += 3 + (Math.random() > 0.82 ? 1 : 0);

        postMessageEnvelope("mjpeg", {
            boundaryCount: state.boundaryCount,
            renderFps: 29 + Math.floor(Math.random() * 3),
            timestamp: new Date().toISOString()
        });
    }, 100);
}

function stopWorker() {
    if (!state.running) {
        return;
    }

    state.running = false;

    if (state.telemetryTimer !== null) {
        self.clearInterval(state.telemetryTimer);
        state.telemetryTimer = null;
    }

    if (state.mjpegTimer !== null) {
        self.clearInterval(state.mjpegTimer);
        state.mjpegTimer = null;
    }
}

self.onmessage = (event) => {
    const command = event.data && event.data.type;

    if (command === "start") {
        startWorker();
        return;
    }

    if (command === "stop") {
        stopWorker();
    }
};
