(() => {
    const landingState = {
        initialized: false,
        ownerToken: null,
        observer: null,
        animationFrame: null,
        resizeHandler: null,
        scrollHandler: null,
        canvas: null,
        context: null,
        particles: []
    };

    const dashboardState = {
        mjpegDecoders: new Map(),
        webrtcSessions: new Map(),
        mockWorker: null,
        mockDotNetRef: null
    };

    function initLanding(ownerToken) {
        const nextOwnerToken = ownerToken || "default";
        if (landingState.initialized && landingState.ownerToken !== nextOwnerToken) {
            tearDownLanding();
        }

        if (landingState.initialized && landingState.ownerToken === nextOwnerToken) {
            setUpRevealObserver();
            return;
        }

        landingState.ownerToken = nextOwnerToken;
        landingState.initialized = true;
        setUpRevealObserver();
        setUpParallaxOffset();
        setUpParticleField();
    }

    function tearDownLanding() {
        if (!landingState.initialized) {
            return;
        }

        if (landingState.observer) {
            landingState.observer.disconnect();
            landingState.observer = null;
        }

        if (landingState.animationFrame) {
            cancelAnimationFrame(landingState.animationFrame);
            landingState.animationFrame = null;
        }

        if (landingState.scrollHandler) {
            window.removeEventListener("scroll", landingState.scrollHandler);
            landingState.scrollHandler = null;
        }

        if (landingState.resizeHandler) {
            window.removeEventListener("resize", landingState.resizeHandler);
            landingState.resizeHandler = null;
        }

        if (landingState.context && landingState.canvas) {
            landingState.context.clearRect(0, 0, landingState.canvas.width, landingState.canvas.height);
        }

        landingState.particles = [];
        landingState.canvas = null;
        landingState.context = null;
        landingState.ownerToken = null;
        landingState.initialized = false;
        document.documentElement.style.removeProperty("--scroll-offset");
    }

    function disposeLanding(ownerToken) {
        if (!landingState.initialized) {
            return;
        }

        const currentOwnerToken = ownerToken || "default";
        if (landingState.ownerToken !== currentOwnerToken) {
            return;
        }

        tearDownLanding();
    }

    function setUpRevealObserver() {
        const targets = document.querySelectorAll("[data-animate]");
        if (!targets.length) {
            return;
        }

        landingState.observer = new IntersectionObserver(
            (entries) => {
                for (const entry of entries) {
                    if (entry.isIntersecting) {
                        entry.target.classList.add("in-view");
                        landingState.observer.unobserve(entry.target);
                    }
                }
            },
            {
                threshold: 0.18,
                rootMargin: "0px 0px -12% 0px"
            }
        );

        for (const target of targets) {
            landingState.observer.observe(target);
        }
    }

    function setUpParallaxOffset() {
        landingState.scrollHandler = () => {
            const offset = window.scrollY * -0.16;
            document.documentElement.style.setProperty("--scroll-offset", `${offset}px`);
        };

        landingState.scrollHandler();
        window.addEventListener("scroll", landingState.scrollHandler, { passive: true });
    }

    function setUpParticleField() {
        landingState.canvas = document.getElementById("particle-canvas");
        if (!(landingState.canvas instanceof HTMLCanvasElement)) {
            return;
        }

        landingState.context = landingState.canvas.getContext("2d");
        if (!landingState.context) {
            return;
        }

        const reset = () => {
            const ratio = window.devicePixelRatio || 1;
            landingState.canvas.width = Math.floor(window.innerWidth * ratio);
            landingState.canvas.height = Math.floor(window.innerHeight * ratio);
            landingState.canvas.style.width = `${window.innerWidth}px`;
            landingState.canvas.style.height = `${window.innerHeight}px`;

            const particleCount = Math.max(20, Math.min(60, Math.floor(window.innerWidth / 34)));
            landingState.particles = Array.from({ length: particleCount }, () => ({
                x: Math.random() * landingState.canvas.width,
                y: Math.random() * landingState.canvas.height,
                vx: (Math.random() - 0.5) * 0.28 * ratio,
                vy: (Math.random() - 0.5) * 0.28 * ratio,
                radius: (Math.random() * 1.7 + 0.8) * ratio
            }));
        };

        landingState.resizeHandler = () => reset();
        window.addEventListener("resize", landingState.resizeHandler);

        reset();
        animateParticleField();
    }

    function animateParticleField() {
        if (!landingState.context || !landingState.canvas) {
            return;
        }

        const ctx = landingState.context;
        const width = landingState.canvas.width;
        const height = landingState.canvas.height;

        ctx.clearRect(0, 0, width, height);

        for (let i = 0; i < landingState.particles.length; i += 1) {
            const particle = landingState.particles[i];
            particle.x += particle.vx;
            particle.y += particle.vy;

            if (particle.x < 0 || particle.x > width) {
                particle.vx *= -1;
            }

            if (particle.y < 0 || particle.y > height) {
                particle.vy *= -1;
            }

            for (let j = i + 1; j < landingState.particles.length; j += 1) {
                const other = landingState.particles[j];
                const dx = particle.x - other.x;
                const dy = particle.y - other.y;
                const distance = Math.sqrt(dx * dx + dy * dy);
                const limit = 140 * (window.devicePixelRatio || 1);

                if (distance < limit) {
                    const alpha = 1 - distance / limit;
                    ctx.strokeStyle = `rgba(45, 246, 255, ${alpha * 0.16})`;
                    ctx.lineWidth = 1;
                    ctx.beginPath();
                    ctx.moveTo(particle.x, particle.y);
                    ctx.lineTo(other.x, other.y);
                    ctx.stroke();
                }
            }

            ctx.fillStyle = "rgba(255, 127, 50, 0.75)";
            ctx.beginPath();
            ctx.arc(particle.x, particle.y, particle.radius, 0, Math.PI * 2);
            ctx.fill();
        }

        landingState.animationFrame = requestAnimationFrame(animateParticleField);
    }

    function concatUint8Arrays(a, b) {
        const merged = new Uint8Array(a.length + b.length);
        merged.set(a);
        merged.set(b, a.length);
        return merged;
    }

    function countPatternOccurrences(data, pattern) {
        if (pattern.length === 0 || data.length < pattern.length) {
            return 0;
        }

        let occurrences = 0;
        for (let index = 0; index <= data.length - pattern.length; index += 1) {
            let match = true;
            for (let offset = 0; offset < pattern.length; offset += 1) {
                if (data[index + offset] !== pattern[offset]) {
                    match = false;
                    break;
                }
            }

            if (match) {
                occurrences += 1;
            }
        }

        return occurrences;
    }

    function scaleCanvas(canvas, context) {
        const rect = canvas.getBoundingClientRect();
        const ratio = window.devicePixelRatio || 1;
        canvas.width = Math.max(1, Math.floor(rect.width * ratio));
        canvas.height = Math.max(1, Math.floor(rect.height * ratio));
        context.setTransform(ratio, 0, 0, ratio, 0, 0);
    }

    function startMockStreamWorker(dotNetRef) {
        stopMockStreamWorker();

        const workerUrl = new URL("js/mock-stream.worker.js", document.baseURI);
        const worker = new Worker(workerUrl, { type: "module" });

        dashboardState.mockWorker = worker;
        dashboardState.mockDotNetRef = dotNetRef;

        worker.onmessage = (event) => {
            if (!dashboardState.mockDotNetRef) {
                return;
            }

            const envelope = event.data || {};
            const messageType = typeof envelope.type === "string" ? envelope.type : "";
            if (!messageType) {
                return;
            }

            let payloadJson = "null";
            try {
                payloadJson = JSON.stringify(envelope.payload ?? null);
            } catch {
                payloadJson = "null";
            }

            dashboardState.mockDotNetRef.invokeMethodAsync("HandleWorkerMessage", messageType, payloadJson).catch(() => {
                // Ignore callback failures during route transitions.
            });
        };

        worker.onerror = () => {
            if (!dashboardState.mockDotNetRef) {
                return;
            }

            dashboardState.mockDotNetRef.invokeMethodAsync("HandleWorkerMessage", "worker-error", "null").catch(() => {
                // Ignore callback failures during route transitions.
            });
        };

        worker.postMessage({ type: "start" });
    }

    function stopMockStreamWorker() {
        if (!dashboardState.mockWorker) {
            return;
        }

        try {
            dashboardState.mockWorker.postMessage({ type: "stop" });
        } catch {
            // Ignore stop races while worker is shutting down.
        }

        dashboardState.mockWorker.terminate();
        dashboardState.mockWorker = null;
        dashboardState.mockDotNetRef = null;
    }

    async function startMjpegDecoder(canvasId, streamUrl = "", dotNetRef = null) {
        stopMjpegDecoder(canvasId);

        const canvas = document.getElementById(canvasId);
        if (!(canvas instanceof HTMLCanvasElement)) {
            return;
        }

        const context = canvas.getContext("2d");
        if (!context) {
            return;
        }

        const state = {
            canvas,
            context,
            dotNetRef,
            streamUrl,
            abortController: typeof streamUrl === "string" && streamUrl.length > 0 ? new AbortController() : null,
            running: true,
            boundaries: 0,
            renderFps: 0,
            frameCounter: 0,
            lastFpsMeasure: performance.now(),
            lastDraw: performance.now(),
            animationFrame: null,
            resizeHandler: null
        };

        state.resizeHandler = () => scaleCanvas(canvas, context);
        window.addEventListener("resize", state.resizeHandler);
        state.resizeHandler();

        dashboardState.mjpegDecoders.set(canvasId, state);

        drawMjpegFrame(state);

        if (typeof streamUrl === "string" && streamUrl.length > 0) {
            try {
                const response = await fetch(streamUrl, {
                    cache: "no-store",
                    signal: state.abortController ? state.abortController.signal : undefined
                });

                if (!response.body) {
                    return;
                }

                const marker = new TextEncoder().encode("--frame");
                const reader = response.body.getReader();
                let carry = new Uint8Array(0);

                while (state.running) {
                    const { done, value } = await reader.read();
                    if (done || !value) {
                        break;
                    }

                    const merged = concatUint8Arrays(carry, value);
                    const matches = countPatternOccurrences(merged, marker);
                    if (matches > 0) {
                        state.boundaries += matches;
                    }

                    carry = merged.slice(Math.max(0, merged.length - marker.length + 1));
                }
            } catch {
                // Ignore stream termination and transient network failures.
            }
        }
    }

    function drawMjpegFrame(state) {
        if (!state.running) {
            return;
        }

        const now = performance.now();
        const frameGap = 1000 / 30;

        if (now - state.lastDraw >= frameGap) {
            state.lastDraw = now;
            state.frameCounter += 1;

            const ctx = state.context;
            const width = state.canvas.clientWidth;
            const height = state.canvas.clientHeight;
            const pulse = (state.boundaries % 240) / 240;

            const gradient = ctx.createLinearGradient(0, 0, width, height);
            gradient.addColorStop(0, "#0d1b3d");
            gradient.addColorStop(1, "#142a5d");
            ctx.fillStyle = gradient;
            ctx.fillRect(0, 0, width, height);

            const boundaryStripeWidth = 6 + Math.round(pulse * 16);
            for (let i = 0; i < width; i += boundaryStripeWidth + 5) {
                ctx.fillStyle = "rgba(45, 246, 255, 0.16)";
                ctx.fillRect(i, 0, boundaryStripeWidth, height);
            }

            const waveformHeight = (Math.sin(now / 140) * 0.28 + 0.46) * height;
            ctx.strokeStyle = "rgba(255, 127, 50, 0.9)";
            ctx.lineWidth = 3;
            ctx.beginPath();
            ctx.moveTo(0, waveformHeight);
            for (let x = 0; x <= width; x += 8) {
                const y = waveformHeight + Math.sin((x + now / 8) / 25) * 11;
                ctx.lineTo(x, y);
            }
            ctx.stroke();

            ctx.fillStyle = "#f7fbff";
            ctx.font = "600 14px Consolas, monospace";
            ctx.fillText(`Frame Boundary #${state.boundaries}`, 16, 24);
            ctx.fillText("Transport: multipart byte stream", 16, 44);
        }

        if (now - state.lastFpsMeasure >= 1000) {
            state.renderFps = state.frameCounter;
            state.frameCounter = 0;
            state.lastFpsMeasure = now;

            if (state.dotNetRef) {
                state.dotNetRef.invokeMethodAsync("UpdateMjpegStats", state.boundaries, state.renderFps).catch(() => {
                    // Ignore callback failures during component teardown.
                });
            }
        }

        state.animationFrame = requestAnimationFrame(() => drawMjpegFrame(state));
    }

    function setMjpegBoundaryCount(canvasId, boundaryCount) {
        const state = dashboardState.mjpegDecoders.get(canvasId);
        if (!state) {
            return;
        }

        state.boundaries = Math.max(0, Math.floor(boundaryCount));
    }

    function stopMjpegDecoder(canvasId) {
        const state = dashboardState.mjpegDecoders.get(canvasId);
        if (!state) {
            return;
        }

        state.running = false;

        if (state.abortController) {
            state.abortController.abort();
        }

        if (state.animationFrame) {
            cancelAnimationFrame(state.animationFrame);
        }

        if (state.resizeHandler) {
            window.removeEventListener("resize", state.resizeHandler);
        }

        dashboardState.mjpegDecoders.delete(canvasId);
    }

    async function startWebRtcDiagnostics(videoId, dotNetRef) {
        stopWebRtcDiagnostics(videoId);

        const video = document.getElementById(videoId);
        if (!(video instanceof HTMLVideoElement)) {
            return;
        }

        const sourceCanvas = document.createElement("canvas");
        sourceCanvas.width = 640;
        sourceCanvas.height = 360;
        const sourceContext = sourceCanvas.getContext("2d");
        if (!sourceContext) {
            return;
        }

        const sourceStream = sourceCanvas.captureStream(30);
        const sender = new RTCPeerConnection();
        const receiver = new RTCPeerConnection();

        const state = {
            video,
            dotNetRef,
            sourceCanvas,
            sourceContext,
            sourceStream,
            sender,
            receiver,
            statsTimer: null,
            drawFrame: null,
            running: true,
            connectionState: "connecting"
        };

        dashboardState.webrtcSessions.set(videoId, state);

        sender.onicecandidate = (event) => {
            if (event.candidate) {
                receiver.addIceCandidate(event.candidate).catch(() => {});
            }
        };

        receiver.onicecandidate = (event) => {
            if (event.candidate) {
                sender.addIceCandidate(event.candidate).catch(() => {});
            }
        };

        receiver.ontrack = (event) => {
            video.srcObject = event.streams[0];
            video.play().catch(() => {});
        };

        const onConnectionStateChange = () => {
            const rtcState = receiver.connectionState || sender.connectionState;
            if (rtcState === "connected") {
                state.connectionState = "connected";
            } else if (rtcState === "failed" || rtcState === "disconnected" || rtcState === "closed") {
                state.connectionState = "degraded";
            } else {
                state.connectionState = "connecting";
            }
        };

        sender.onconnectionstatechange = onConnectionStateChange;
        receiver.onconnectionstatechange = onConnectionStateChange;

        for (const track of sourceStream.getTracks()) {
            sender.addTrack(track, sourceStream);
        }

        drawWebRtcSource(state);

        try {
            const offer = await sender.createOffer();
            await sender.setLocalDescription(offer);
            await receiver.setRemoteDescription(offer);

            const answer = await receiver.createAnswer();
            await receiver.setLocalDescription(answer);
            await sender.setRemoteDescription(answer);
        } catch {
            state.connectionState = "degraded";
        }

        state.statsTimer = setInterval(async () => {
            if (!state.running) {
                return;
            }

            let fps = 0;
            let jitterMs = 0;
            let packetLossPercent = 0;

            try {
                const stats = await receiver.getStats();
                stats.forEach((report) => {
                    if (report.type === "inbound-rtp" && report.kind === "video") {
                        if (typeof report.framesPerSecond === "number") {
                            fps = report.framesPerSecond;
                        }

                        if (typeof report.jitter === "number") {
                            jitterMs = report.jitter * 1000;
                        }

                        const packetsLost = typeof report.packetsLost === "number" ? report.packetsLost : 0;
                        const packetsReceived = typeof report.packetsReceived === "number" ? report.packetsReceived : 0;
                        const totalPackets = packetsLost + packetsReceived;
                        packetLossPercent = totalPackets > 0 ? (packetsLost / totalPackets) * 100 : 0;
                    }
                });
            } catch {
                state.connectionState = "degraded";
            }

            if (state.dotNetRef) {
                state.dotNetRef.invokeMethodAsync(
                    "UpdateWebRtcStats",
                    Math.round(fps * 10) / 10,
                    Math.round(jitterMs * 10) / 10,
                    Math.round(packetLossPercent * 100) / 100,
                    state.connectionState
                ).catch(() => {
                    // Ignore callback failures during component teardown.
                });
            }
        }, 1000);
    }

    function drawWebRtcSource(state) {
        if (!state.running) {
            return;
        }

        const now = performance.now();
        const ctx = state.sourceContext;
        const width = state.sourceCanvas.width;
        const height = state.sourceCanvas.height;

        const gradient = ctx.createLinearGradient(0, 0, width, height);
        gradient.addColorStop(0, "#0e1d40");
        gradient.addColorStop(1, "#17356f");
        ctx.fillStyle = gradient;
        ctx.fillRect(0, 0, width, height);

        const scanOffset = (now / 7) % width;
        ctx.fillStyle = "rgba(45, 246, 255, 0.18)";
        ctx.fillRect(scanOffset, 0, 42, height);

        for (let index = 0; index < 8; index += 1) {
            const x = (index / 8) * width;
            const y = height * 0.55 + Math.sin((now / 320) + index) * 30;
            ctx.fillStyle = "rgba(255, 127, 50, 0.72)";
            ctx.beginPath();
            ctx.arc(x, y, 7, 0, Math.PI * 2);
            ctx.fill();
        }

        ctx.fillStyle = "#f2f8ff";
        ctx.font = "600 22px Consolas, monospace";
        ctx.fillText("Synthetic Track", 20, 36);
        ctx.font = "500 14px Consolas, monospace";
        ctx.fillText(new Date().toISOString(), 20, 58);

        state.drawFrame = requestAnimationFrame(() => drawWebRtcSource(state));
    }

    function stopWebRtcDiagnostics(videoId) {
        const state = dashboardState.webrtcSessions.get(videoId);
        if (!state) {
            return;
        }

        state.running = false;

        if (state.statsTimer) {
            clearInterval(state.statsTimer);
        }

        if (state.drawFrame) {
            cancelAnimationFrame(state.drawFrame);
        }

        if (state.sourceStream) {
            for (const track of state.sourceStream.getTracks()) {
                track.stop();
            }
        }

        if (state.sender) {
            state.sender.close();
        }

        if (state.receiver) {
            state.receiver.close();
        }

        if (state.video) {
            state.video.srcObject = null;
        }

        dashboardState.webrtcSessions.delete(videoId);
    }

    function disposeAllDashboard() {
        stopMockStreamWorker();

        const mjpegIds = Array.from(dashboardState.mjpegDecoders.keys());
        for (const decoderId of mjpegIds) {
            stopMjpegDecoder(decoderId);
        }

        const webrtcIds = Array.from(dashboardState.webrtcSessions.keys());
        for (const sessionId of webrtcIds) {
            stopWebRtcDiagnostics(sessionId);
        }
    }

    window.cvLanding = {
        init: initLanding,
        dispose: disposeLanding
    };

    window.cvDashboard = {
        startMockStreamWorker,
        stopMockStreamWorker,
        startMjpegDecoder,
        setMjpegBoundaryCount,
        stopMjpegDecoder,
        startWebRtcDiagnostics,
        stopWebRtcDiagnostics,
        disposeAll: disposeAllDashboard
    };
})();
