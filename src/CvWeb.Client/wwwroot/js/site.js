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
        gpuAlignmentSessions: new Map(),
        telemetryGridSessions: new Map(),
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

    function decodeBase64ToUint8Array(base64) {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let index = 0; index < binary.length; index += 1) {
            bytes[index] = binary.charCodeAt(index);
        }

        return bytes;
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
            const envelope = event.data || {};
            const messageType = typeof envelope.type === "string" ? envelope.type : "";
            if (!messageType) {
                return;
            }

            if (messageType === "telemetry-grid") {
                routeTelemetryGridFrame(envelope.payload);
                return;
            }

            if (!dashboardState.mockDotNetRef) {
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

        for (const telemetryGridState of dashboardState.telemetryGridSessions.values()) {
            telemetryGridState.streamState = "waiting";
        }
    }

    function buildTelemetryPalette() {
        const palette = new Uint8ClampedArray(256 * 4);

        for (let index = 0; index < 256; index += 1) {
            const t = index / 255;
            const r = Math.round(14 + t * 224);
            const g = Math.round(36 + Math.sin(t * Math.PI) * 130);
            const b = Math.round(56 + (1 - t) * 166);
            const offset = index * 4;

            palette[offset] = r;
            palette[offset + 1] = g;
            palette[offset + 2] = b;
            palette[offset + 3] = 255;
        }

        return palette;
    }

    function createTelemetryRing(capacity, sensorCount) {
        return Array.from({ length: capacity }, () => ({
            intensities: new Uint8Array(sensorCount),
            alerts: new Uint8Array(sensorCount),
            sequence: 0,
            alertCount: 0,
            cpuAveragePercent: 0,
            packetLossAveragePercent: 0,
            timestamp: ""
        }));
    }

    function startTelemetryGrid(canvasId, dotNetRef, gridWidth = 32, gridHeight = 32) {
        stopTelemetryGrid(canvasId);

        const canvas = document.getElementById(canvasId);
        if (!(canvas instanceof HTMLCanvasElement)) {
            return;
        }

        const context = canvas.getContext("2d");
        if (!context) {
            return;
        }

        const safeGridWidth = Math.max(1, Math.floor(gridWidth));
        const safeGridHeight = Math.max(1, Math.floor(gridHeight));
        const sensorCount = safeGridWidth * safeGridHeight;
        const ringCapacity = 256;
        const ringMask = ringCapacity - 1;

        const offscreenCanvas = document.createElement("canvas");
        offscreenCanvas.width = safeGridWidth;
        offscreenCanvas.height = safeGridHeight;

        const offscreenContext = offscreenCanvas.getContext("2d", {
            alpha: false
        });

        if (!offscreenContext) {
            return;
        }

        const state = {
            canvas,
            context,
            dotNetRef,
            gridWidth: safeGridWidth,
            gridHeight: safeGridHeight,
            sensorCount,
            ring: createTelemetryRing(ringCapacity, sensorCount),
            ringCapacity,
            ringMask,
            writeIndex: 0,
            bufferedFrames: 0,
            droppedFrames: 0,
            latestSequence: 0,
            latestAlertCount: 0,
            latestCpuAveragePercent: 0,
            latestPacketLossAveragePercent: 0,
            palette: buildTelemetryPalette(),
            offscreenCanvas,
            offscreenContext,
            imageData: offscreenContext.createImageData(safeGridWidth, safeGridHeight),
            ingestCounter: 0,
            renderCounter: 0,
            ingestRateHz: 0,
            renderRateFps: 0,
            statsWindowStartedAt: performance.now(),
            lastFrameAt: performance.now(),
            animationFrame: null,
            resizeHandler: null,
            streamState: dashboardState.mockWorker ? "connected" : "waiting"
        };

        state.resizeHandler = () => scaleCanvas(canvas, context);
        window.addEventListener("resize", state.resizeHandler, { passive: true });
        state.resizeHandler();

        dashboardState.telemetryGridSessions.set(canvasId, state);
        state.animationFrame = requestAnimationFrame(() => drawTelemetryGrid(state));
    }

    function stopTelemetryGrid(canvasId) {
        const state = dashboardState.telemetryGridSessions.get(canvasId);
        if (!state) {
            return;
        }

        if (state.animationFrame) {
            cancelAnimationFrame(state.animationFrame);
        }

        if (state.resizeHandler) {
            window.removeEventListener("resize", state.resizeHandler);
        }

        dashboardState.telemetryGridSessions.delete(canvasId);
    }

    function routeTelemetryGridFrame(payload) {
        if (!payload) {
            return;
        }

        for (const session of dashboardState.telemetryGridSessions.values()) {
            ingestTelemetryGridFrame(session, payload);
        }
    }

    function ingestTelemetryGridFrame(session, payload) {
        const sourceIntensities = payload.intensities;
        const sourceAlerts = payload.alerts;

        if (!sourceIntensities || !sourceAlerts) {
            return;
        }

        if (sourceIntensities.length < session.sensorCount || sourceAlerts.length < session.sensorCount) {
            return;
        }

        const slot = session.ring[session.writeIndex];

        slot.intensities.set(sourceIntensities.subarray(0, session.sensorCount));
        slot.alerts.set(sourceAlerts.subarray(0, session.sensorCount));

        slot.sequence = typeof payload.sequence === "number" ? payload.sequence : session.latestSequence + 1;
        slot.alertCount = typeof payload.alertCount === "number" ? payload.alertCount : 0;
        slot.cpuAveragePercent = typeof payload.cpuAveragePercent === "number" ? payload.cpuAveragePercent : 0;
        slot.packetLossAveragePercent = typeof payload.packetLossAveragePercent === "number" ? payload.packetLossAveragePercent : 0;
        slot.timestamp = typeof payload.timestamp === "string" ? payload.timestamp : "";

        session.latestSequence = slot.sequence;
        session.latestAlertCount = slot.alertCount;
        session.latestCpuAveragePercent = slot.cpuAveragePercent;
        session.latestPacketLossAveragePercent = slot.packetLossAveragePercent;
        session.lastFrameAt = performance.now();
        session.streamState = "connected";

        session.writeIndex = (session.writeIndex + 1) & session.ringMask;
        if (session.bufferedFrames < session.ringCapacity) {
            session.bufferedFrames += 1;
        } else {
            session.droppedFrames += 1;
        }

        session.ingestCounter += 1;
    }

    function drawTelemetryGrid(state) {
        if (!dashboardState.telemetryGridSessions.has(state.canvas.id)) {
            return;
        }

        const now = performance.now();

        if (state.bufferedFrames > 0) {
            const latestIndex = (state.writeIndex - 1 + state.ringCapacity) & state.ringMask;
            const latestFrame = state.ring[latestIndex];

            state.bufferedFrames = 0;
            renderTelemetryGridFrame(state, latestFrame);
            state.renderCounter += 1;
        } else if (now - state.lastFrameAt > 1500) {
            state.streamState = dashboardState.mockWorker ? "degraded" : "waiting";
        }

        if (now - state.statsWindowStartedAt >= 1000) {
            const elapsedSeconds = (now - state.statsWindowStartedAt) / 1000;
            state.ingestRateHz = state.ingestCounter / elapsedSeconds;
            state.renderRateFps = state.renderCounter / elapsedSeconds;
            state.ingestCounter = 0;
            state.renderCounter = 0;
            state.statsWindowStartedAt = now;

            if (state.dotNetRef) {
                state.dotNetRef.invokeMethodAsync(
                    "UpdateTelemetryGridStats",
                    state.sensorCount,
                    Math.round(state.ingestRateHz * 10) / 10,
                    Math.round(state.renderRateFps * 10) / 10,
                    state.droppedFrames,
                    state.latestAlertCount,
                    Math.round(state.latestCpuAveragePercent * 10) / 10,
                    Math.round(state.latestPacketLossAveragePercent * 1000) / 1000,
                    state.streamState,
                    state.latestSequence
                ).catch(() => {
                    // Ignore callback failures during component teardown.
                });
            }
        }

        state.animationFrame = requestAnimationFrame(() => drawTelemetryGrid(state));
    }

    function renderTelemetryGridFrame(state, frame) {
        const pixels = state.imageData.data;
        const palette = state.palette;

        for (let index = 0; index < state.sensorCount; index += 1) {
            const pixelOffset = index * 4;
            const paletteOffset = frame.intensities[index] * 4;
            const alert = frame.alerts[index];

            let red = palette[paletteOffset];
            let green = palette[paletteOffset + 1];
            let blue = palette[paletteOffset + 2];

            if (alert === 1) {
                red = Math.min(255, red + 36);
                green = Math.min(255, green + 16);
            } else if (alert >= 2) {
                red = 255;
                green = 104;
                blue = 84;
            }

            pixels[pixelOffset] = red;
            pixels[pixelOffset + 1] = green;
            pixels[pixelOffset + 2] = blue;
            pixels[pixelOffset + 3] = 255;
        }

        state.offscreenContext.putImageData(state.imageData, 0, 0);

        const width = state.canvas.clientWidth;
        const height = state.canvas.clientHeight;
        const context = state.context;

        context.save();
        context.clearRect(0, 0, width, height);
        context.imageSmoothingEnabled = false;
        context.drawImage(state.offscreenCanvas, 0, 0, state.gridWidth, state.gridHeight, 0, 0, width, height);

        context.fillStyle = "rgba(8, 14, 31, 0.56)";
        context.fillRect(10, 10, 252, 52);
        context.strokeStyle = "rgba(45, 246, 255, 0.32)";
        context.lineWidth = 1;
        context.strokeRect(10, 10, 252, 52);

        context.fillStyle = "#f3f8ff";
        context.font = "600 12px Consolas, monospace";
        context.fillText(`SEQ ${frame.sequence} | ALERTS ${frame.alertCount}`, 18, 30);
        context.fillText(`${state.gridWidth}x${state.gridHeight} SENSOR MESH`, 18, 48);
        context.restore();
    }

    function createGlShader(gl, shaderType, source) {
        const shader = gl.createShader(shaderType);
        if (!shader) {
            throw new Error("Unable to allocate shader.");
        }

        gl.shaderSource(shader, source);
        gl.compileShader(shader);

        if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
            const info = gl.getShaderInfoLog(shader) || "Unknown shader compilation error.";
            gl.deleteShader(shader);
            throw new Error(info);
        }

        return shader;
    }

    function createGlProgram(gl, vertexSource, fragmentSource) {
        const vertexShader = createGlShader(gl, gl.VERTEX_SHADER, vertexSource);
        const fragmentShader = createGlShader(gl, gl.FRAGMENT_SHADER, fragmentSource);
        const program = gl.createProgram();

        if (!program) {
            gl.deleteShader(vertexShader);
            gl.deleteShader(fragmentShader);
            throw new Error("Unable to allocate shader program.");
        }

        gl.attachShader(program, vertexShader);
        gl.attachShader(program, fragmentShader);
        gl.linkProgram(program);

        gl.deleteShader(vertexShader);
        gl.deleteShader(fragmentShader);

        if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
            const info = gl.getProgramInfoLog(program) || "Unknown program link error.";
            gl.deleteProgram(program);
            throw new Error(info);
        }

        return program;
    }

    function createTextureFromImage(gl, imageLike) {
        const texture = gl.createTexture();
        if (!texture) {
            throw new Error("Unable to allocate texture.");
        }

        gl.bindTexture(gl.TEXTURE_2D, texture);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
        gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, imageLike);
        gl.bindTexture(gl.TEXTURE_2D, null);

        return texture;
    }

    function createRenderTarget(gl, width, height) {
        const texture = gl.createTexture();
        if (!texture) {
            throw new Error("Unable to allocate render target texture.");
        }

        gl.bindTexture(gl.TEXTURE_2D, texture);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);

        const framebuffer = gl.createFramebuffer();
        if (!framebuffer) {
            gl.deleteTexture(texture);
            throw new Error("Unable to allocate framebuffer.");
        }

        gl.bindFramebuffer(gl.FRAMEBUFFER, framebuffer);
        gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, texture, 0);

        if (gl.checkFramebufferStatus(gl.FRAMEBUFFER) !== gl.FRAMEBUFFER_COMPLETE) {
            gl.bindFramebuffer(gl.FRAMEBUFFER, null);
            gl.deleteFramebuffer(framebuffer);
            gl.deleteTexture(texture);
            throw new Error("Framebuffer is incomplete.");
        }

        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
        gl.bindTexture(gl.TEXTURE_2D, null);

        return { framebuffer, texture };
    }

    async function loadBitmap(url) {
        const response = await fetch(url, { cache: "force-cache" });
        if (!response.ok) {
            throw new Error(`Failed to load texture: ${url}`);
        }

        const blob = await response.blob();
        return createImageBitmap(blob, {
            colorSpaceConversion: "none",
            premultiplyAlpha: "none"
        });
    }

    function configureQuadBuffer(gl) {
        const quad = gl.createBuffer();
        if (!quad) {
            throw new Error("Unable to allocate quad buffer.");
        }

        gl.bindBuffer(gl.ARRAY_BUFFER, quad);
        gl.bufferData(
            gl.ARRAY_BUFFER,
            new Float32Array([
                -1, -1,
                1, -1,
                -1, 1,
                1, 1
            ]),
            gl.STATIC_DRAW
        );
        gl.bindBuffer(gl.ARRAY_BUFFER, null);

        return quad;
    }

    function setViewportToDisplaySize(gl, canvas) {
        const ratio = window.devicePixelRatio || 1;
        const displayWidth = Math.max(1, Math.floor(canvas.clientWidth * ratio));
        const displayHeight = Math.max(1, Math.floor(canvas.clientHeight * ratio));

        if (canvas.width !== displayWidth || canvas.height !== displayHeight) {
            canvas.width = displayWidth;
            canvas.height = displayHeight;
        }

        gl.viewport(0, 0, canvas.width, canvas.height);
    }

    function drawFullscreenPass(gl, program, quadBuffer) {
        const positionLocation = gl.getAttribLocation(program, "aPosition");

        gl.useProgram(program);
        gl.bindBuffer(gl.ARRAY_BUFFER, quadBuffer);
        gl.enableVertexAttribArray(positionLocation);
        gl.vertexAttribPointer(positionLocation, 2, gl.FLOAT, false, 0, 0);
        gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
        gl.disableVertexAttribArray(positionLocation);
        gl.bindBuffer(gl.ARRAY_BUFFER, null);
    }

    function runGpuAlignmentPass(state) {
        const { gl } = state;
        const passStart = performance.now();

        gl.bindFramebuffer(gl.FRAMEBUFFER, state.framebuffer);
        gl.viewport(0, 0, state.textureWidth, state.textureHeight);
        gl.activeTexture(gl.TEXTURE0);
        gl.bindTexture(gl.TEXTURE_2D, state.textureA);
        gl.activeTexture(gl.TEXTURE1);
        gl.bindTexture(gl.TEXTURE_2D, state.textureB);

        gl.useProgram(state.compareProgram);
        gl.uniform1i(state.compareTextureALocation, 0);
        gl.uniform1i(state.compareTextureBLocation, 1);
        drawFullscreenPass(gl, state.compareProgram, state.quadBuffer);

        if (!state.readback || state.readback.length !== state.comparedBytes) {
            state.readback = new Uint8Array(state.comparedBytes);
        }

        gl.readPixels(0, 0, state.textureWidth, state.textureHeight, gl.RGBA, gl.UNSIGNED_BYTE, state.readback);

        let changedBytes = 0;
        let mismatchedPixels = 0;
        for (let index = 0; index < state.readback.length; index += 4) {
            const r = state.readback[index] > 0 ? 1 : 0;
            const g = state.readback[index + 1] > 0 ? 1 : 0;
            const b = state.readback[index + 2] > 0 ? 1 : 0;
            const a = state.readback[index + 3] > 0 ? 1 : 0;
            const sum = r + g + b + a;

            changedBytes += sum;
            if (sum > 0) {
                mismatchedPixels += 1;
            }
        }

        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
        setViewportToDisplaySize(gl, state.canvas);
        gl.activeTexture(gl.TEXTURE0);
        gl.bindTexture(gl.TEXTURE_2D, state.outputTexture);
        gl.useProgram(state.previewProgram);
        gl.uniform1i(state.previewTextureLocation, 0);
        drawFullscreenPass(gl, state.previewProgram, state.quadBuffer);

        return {
            changedBytes,
            mismatchedPixels,
            comparedBytes: state.comparedBytes,
            elapsedMilliseconds: performance.now() - passStart,
            textureWidth: state.textureWidth,
            textureHeight: state.textureHeight
        };
    }

    async function startGpuAlignmentChecker(canvasId, dotNetRef, textureAUrl, textureBUrl) {
        stopGpuAlignmentChecker(canvasId);

        const canvas = document.getElementById(canvasId);
        if (!(canvas instanceof HTMLCanvasElement)) {
            return;
        }

        const gl = canvas.getContext("webgl2", {
            alpha: false,
            antialias: false,
            depth: false,
            preserveDrawingBuffer: false
        });

        if (!gl) {
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync("UpdateGpuAlignmentFailure", "WebGL2 is not supported in this browser.").catch(() => {
                    // Ignore callback failures during route transitions.
                });
            }

            return;
        }

        const vertexShaderSource = `#version 300 es
in vec2 aPosition;
out vec2 vUv;
void main() {
    vUv = (aPosition + 1.0) * 0.5;
    gl_Position = vec4(aPosition, 0.0, 1.0);
}`;

        const compareFragmentSource = `#version 300 es
precision highp float;
precision highp int;
uniform sampler2D uTextureA;
uniform sampler2D uTextureB;
in vec2 vUv;
out vec4 outColor;
void main() {
    ivec4 a = ivec4(round(texture(uTextureA, vUv) * 255.0));
    ivec4 b = ivec4(round(texture(uTextureB, vUv) * 255.0));
    bvec4 mismatch = notEqual(a, b);
    outColor = vec4(mismatch);
}`;

        const previewFragmentSource = `#version 300 es
precision highp float;
uniform sampler2D uMismatchTexture;
in vec2 vUv;
out vec4 outColor;
void main() {
    vec4 mismatch = texture(uMismatchTexture, vUv);
    float intensity = clamp((mismatch.r + mismatch.g + mismatch.b + mismatch.a) * 0.25, 0.0, 1.0);
    vec3 base = vec3(0.05, 0.11, 0.2);
    vec3 hot = vec3(1.0, 0.5, 0.2);
    vec3 color = mix(base, hot, intensity);
    outColor = vec4(color, 1.0);
}`;

        let bitmapA;
        let bitmapB;

        try {
            [bitmapA, bitmapB] = await Promise.all([
                loadBitmap(textureAUrl),
                loadBitmap(textureBUrl)
            ]);
        } catch {
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync("UpdateGpuAlignmentFailure", "Failed to load 4K reference textures.").catch(() => {
                    // Ignore callback failures during route transitions.
                });
            }
            return;
        }

        const textureWidth = Math.min(bitmapA.width, bitmapB.width);
        const textureHeight = Math.min(bitmapA.height, bitmapB.height);

        if (textureWidth < 3840 || textureHeight < 2160) {
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync("UpdateGpuAlignmentFailure", "Reference textures must be 4K (3840x2160) or larger.").catch(() => {
                    // Ignore callback failures during route transitions.
                });
            }

            bitmapA.close();
            bitmapB.close();
            return;
        }

        try {
            const compareProgram = createGlProgram(gl, vertexShaderSource, compareFragmentSource);
            const previewProgram = createGlProgram(gl, vertexShaderSource, previewFragmentSource);
            const quadBuffer = configureQuadBuffer(gl);
            const textureA = createTextureFromImage(gl, bitmapA);
            const textureB = createTextureFromImage(gl, bitmapB);
            const renderTarget = createRenderTarget(gl, textureWidth, textureHeight);

            const state = {
                canvas,
                gl,
                dotNetRef,
                quadBuffer,
                compareProgram,
                previewProgram,
                compareTextureALocation: gl.getUniformLocation(compareProgram, "uTextureA"),
                compareTextureBLocation: gl.getUniformLocation(compareProgram, "uTextureB"),
                previewTextureLocation: gl.getUniformLocation(previewProgram, "uMismatchTexture"),
                textureA,
                textureB,
                framebuffer: renderTarget.framebuffer,
                outputTexture: renderTarget.texture,
                textureWidth,
                textureHeight,
                comparedBytes: textureWidth * textureHeight * 4,
                readback: null,
                resizeHandler: null
            };

            state.resizeHandler = () => {
                setViewportToDisplaySize(gl, canvas);
                gl.activeTexture(gl.TEXTURE0);
                gl.bindTexture(gl.TEXTURE_2D, state.outputTexture);
                gl.useProgram(state.previewProgram);
                gl.uniform1i(state.previewTextureLocation, 0);
                drawFullscreenPass(gl, state.previewProgram, state.quadBuffer);
            };

            window.addEventListener("resize", state.resizeHandler, { passive: true });
            dashboardState.gpuAlignmentSessions.set(canvasId, state);

            const result = runGpuAlignmentPass(state);

            if (dotNetRef) {
                dotNetRef.invokeMethodAsync(
                    "UpdateGpuAlignmentResult",
                    result.changedBytes,
                    result.comparedBytes,
                    result.mismatchedPixels,
                    Math.round(result.elapsedMilliseconds * 100) / 100,
                    "webgl2-fragment",
                    result.textureWidth,
                    result.textureHeight
                ).catch(() => {
                    // Ignore callback failures during route transitions.
                });
            }

            state.resizeHandler();
        } catch {
            stopGpuAlignmentChecker(canvasId);
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync("UpdateGpuAlignmentFailure", "GPU comparison shader failed to execute.").catch(() => {
                    // Ignore callback failures during route transitions.
                });
            }
        } finally {
            bitmapA.close();
            bitmapB.close();
        }
    }

    function stopGpuAlignmentChecker(canvasId) {
        const state = dashboardState.gpuAlignmentSessions.get(canvasId);
        if (!state) {
            return;
        }

        const { gl } = state;

        if (state.resizeHandler) {
            window.removeEventListener("resize", state.resizeHandler);
        }

        if (state.quadBuffer) {
            gl.deleteBuffer(state.quadBuffer);
        }

        if (state.textureA) {
            gl.deleteTexture(state.textureA);
        }

        if (state.textureB) {
            gl.deleteTexture(state.textureB);
        }

        if (state.outputTexture) {
            gl.deleteTexture(state.outputTexture);
        }

        if (state.framebuffer) {
            gl.deleteFramebuffer(state.framebuffer);
        }

        if (state.compareProgram) {
            gl.deleteProgram(state.compareProgram);
        }

        if (state.previewProgram) {
            gl.deleteProgram(state.previewProgram);
        }

        dashboardState.gpuAlignmentSessions.delete(canvasId);
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

    async function drawMjpegFrameBytes(canvasId, frameBase64) {
        const state = dashboardState.mjpegDecoders.get(canvasId);
        if (!state || typeof frameBase64 !== "string" || frameBase64.length === 0) {
            return;
        }

        try {
            const bytes = decodeBase64ToUint8Array(frameBase64);
            const blob = new Blob([bytes], { type: "image/jpeg" });
            const bitmap = await createImageBitmap(blob);

            const ctx = state.context;
            const width = state.canvas.clientWidth;
            const height = state.canvas.clientHeight;

            ctx.clearRect(0, 0, width, height);
            ctx.drawImage(bitmap, 0, 0, width, height);
            bitmap.close();
        } catch {
            // Ignore malformed frame bytes and transient decode failures.
        }
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

        const gpuIds = Array.from(dashboardState.gpuAlignmentSessions.keys());
        for (const gpuId of gpuIds) {
            stopGpuAlignmentChecker(gpuId);
        }

        const mjpegIds = Array.from(dashboardState.mjpegDecoders.keys());
        for (const decoderId of mjpegIds) {
            stopMjpegDecoder(decoderId);
        }

        const telemetryGridIds = Array.from(dashboardState.telemetryGridSessions.keys());
        for (const telemetryGridId of telemetryGridIds) {
            stopTelemetryGrid(telemetryGridId);
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
        startGpuAlignmentChecker,
        stopGpuAlignmentChecker,
        startMjpegDecoder,
        drawMjpegFrameBytes,
        setMjpegBoundaryCount,
        stopMjpegDecoder,
        startTelemetryGrid,
        stopTelemetryGrid,
        startWebRtcDiagnostics,
        stopWebRtcDiagnostics,
        disposeAll: disposeAllDashboard
    };
})();
