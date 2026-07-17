// ps3waves.js — Motor de ondas animadas estilo PS3 XMB
// Coloca este archivo en: wwwroot/js/ps3waves.js
// Referéncialo en App.razor o _Host.cshtml con:
//   <script src="/js/ps3waves.js"></script>

window.PS3Waves = (function () {
    const _loops = {};

    const WAVE_CONFIGS = [
        { amp: 100, freq: 0.00124, speed: 1.04, rise: 3.9, yRatio: 0.97, band: 58, fillOpacity: 0.15, edgeOpacity: 0.11, glowOpacity: 0.14, textureOpacity: 0.048, lineWidth: 1.45, color: [88, 188, 224], secondary: 0.28, tertiary: 0.11, float: 24, floatSpeed: 0.16, phase: 0.3, seed: 1.2, warp: 28, warpFreq: 0.00052, warpSpeed: 0.42, thicknessPulse: 0.3, thicknessFreq: 0.00082, thicknessSpeed: 0.52, meander: 14, meanderSpeed: 0.23, grain: 8.5, grainFreq: 0.0074, grainSpeed: 0.44 },
        { amp: 84, freq: 0.0017, speed: 1.32, rise: 5.5, yRatio: 0.81, band: 48, fillOpacity: 0.12, edgeOpacity: 0.1, glowOpacity: 0.12, textureOpacity: 0.041, lineWidth: 1.28, color: [102, 150, 226], secondary: 0.31, tertiary: 0.12, float: 18, floatSpeed: 0.19, phase: 1.35, seed: 2.8, warp: 24, warpFreq: 0.00065, warpSpeed: 0.52, thicknessPulse: 0.28, thicknessFreq: 0.00105, thicknessSpeed: 0.59, meander: 12, meanderSpeed: 0.28, grain: 7.2, grainFreq: 0.0085, grainSpeed: 0.52 },
        { amp: 68, freq: 0.0022, speed: 1.62, rise: 6.8, yRatio: 0.65, band: 38, fillOpacity: 0.09, edgeOpacity: 0.085, glowOpacity: 0.1, textureOpacity: 0.036, lineWidth: 1.14, color: [118, 132, 214], secondary: 0.34, tertiary: 0.14, float: 13, floatSpeed: 0.22, phase: 2.4, seed: 4.1, warp: 20, warpFreq: 0.00084, warpSpeed: 0.64, thicknessPulse: 0.25, thicknessFreq: 0.00128, thicknessSpeed: 0.72, meander: 9, meanderSpeed: 0.33, grain: 5.8, grainFreq: 0.0096, grainSpeed: 0.61 },
        { amp: 54, freq: 0.0029, speed: 1.94, rise: 8.2, yRatio: 0.46, band: 29, fillOpacity: 0.07, edgeOpacity: 0.07, glowOpacity: 0.08, textureOpacity: 0.03, lineWidth: 1.02, color: [136, 138, 198], secondary: 0.36, tertiary: 0.15, float: 10, floatSpeed: 0.26, phase: 3.55, seed: 5.4, warp: 16, warpFreq: 0.00106, warpSpeed: 0.79, thicknessPulse: 0.23, thicknessFreq: 0.00156, thicknessSpeed: 0.88, meander: 7, meanderSpeed: 0.37, grain: 4.7, grainFreq: 0.0108, grainSpeed: 0.73 },
        { amp: 41, freq: 0.00364, speed: 2.2, rise: 9.5, yRatio: 0.28, band: 22, fillOpacity: 0.05, edgeOpacity: 0.055, glowOpacity: 0.06, textureOpacity: 0.024, lineWidth: 0.92, color: [176, 190, 225], secondary: 0.4, tertiary: 0.15, float: 7, floatSpeed: 0.3, phase: 4.5, seed: 6.7, warp: 12, warpFreq: 0.00134, warpSpeed: 0.95, thicknessPulse: 0.2, thicknessFreq: 0.0019, thicknessSpeed: 1.02, meander: 5, meanderSpeed: 0.42, grain: 3.9, grainFreq: 0.012, grainSpeed: 0.84 }
    ];

    function rgba(color, alpha) {
        return `rgba(${color[0]}, ${color[1]}, ${color[2]}, ${alpha})`;
    }

    function brighten(color, amount) {
        return color.map(channel => Math.min(255, Math.round(channel + (255 - channel) * amount)));
    }

    function organicNoise(x, t, seed) {
        return (
            Math.sin(x + t + seed) * 0.42 +
            Math.cos(x * 1.83 - t * 1.17 + seed * 1.7) * 0.28 +
            Math.sin(x * 3.14 + t * 0.46 + seed * 2.4) * 0.18 +
            Math.cos(x * 5.32 - t * 0.24 + seed * 3.1) * 0.12
        );
    }

    function createNoiseTile(size) {
        const canvas = document.createElement('canvas');
        canvas.width = size;
        canvas.height = size;

        const ctx = canvas.getContext('2d', { alpha: true });
        if (!ctx) return null;

        const imageData = ctx.createImageData(size, size);
        const data = imageData.data;

        for (let i = 0; i < data.length; i += 4) {
            const roll = Math.random();
            const tint = 198 + Math.floor(Math.random() * 30);
            data[i] = tint - 12;
            data[i + 1] = tint - 4;
            data[i + 2] = tint + 10;
            data[i + 3] = Math.floor(Math.pow(roll, 3.4) * 32);
        }

        ctx.putImageData(imageData, 0, 0);
        return canvas;
    }

    function resize(state) {
        const parent = state.canvas.parentElement;
        if (!parent) return;

        const bounds = parent.getBoundingClientRect();
        const width = Math.max(1, Math.round(bounds.width || parent.offsetWidth || 400));
        const height = Math.max(1, Math.round(bounds.height || parent.offsetHeight || 600));
        const dpr = Math.min(window.devicePixelRatio || 1, 1.5);

        state.width = width;
        state.height = height;
        state.canvas.width = Math.round(width * dpr);
        state.canvas.height = Math.round(height * dpr);
        state.canvas.style.width = `${width}px`;
        state.canvas.style.height = `${height}px`;
        state.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        state.ctx.lineCap = 'round';
        state.ctx.lineJoin = 'round';
    }

    function buildLayerPoints(layer, width, height, elapsedSeconds) {
        const overflow = Math.max(70, width * 0.18);
        const step = Math.max(4, Math.round(width / 280));
        const riseSpan = height * 0.34 + layer.amp * 1.8 + layer.band * 2.8;
        const riseOffset = (elapsedSeconds * layer.rise) % riseSpan;
        let baseY = height * layer.yRatio
            - riseOffset
            + Math.sin(elapsedSeconds * layer.floatSpeed + layer.phase) * layer.float
            + Math.sin(elapsedSeconds * layer.meanderSpeed + layer.seed) * layer.meander;

        if (baseY < -(layer.band * 3 + layer.amp * 1.2)) {
            baseY += riseSpan + height * 0.12 + layer.band * 2.5;
        }

        const points = [];

        for (let x = -overflow; x <= width + overflow; x += step) {
            const warpA = Math.sin(x * layer.warpFreq - elapsedSeconds * layer.warpSpeed + layer.seed);
            const warpB = Math.cos(x * layer.warpFreq * 0.47 + elapsedSeconds * layer.warpSpeed * 0.61 + layer.phase * 1.4);
            const phaseNoise = warpA * 0.55 + warpB * 0.45;
            const sampleX = x + warpA * layer.warp + warpB * layer.warp * 0.58;
            const primary = Math.sin(sampleX * layer.freq - elapsedSeconds * layer.speed + layer.phase + phaseNoise * 0.34) * layer.amp;
            const secondary = Math.sin(sampleX * layer.freq * 0.46 - elapsedSeconds * layer.speed * 0.68 + layer.phase * 1.7 + warpB * 0.22) * (layer.amp * layer.secondary);
            const tertiary = Math.cos(sampleX * layer.freq * 0.18 - elapsedSeconds * layer.speed * 0.38 + layer.phase * 0.4 + warpA * 0.18) * (layer.amp * layer.tertiary);
            const lift = phaseNoise * layer.amp * 0.08;
            const thicknessA = Math.sin(sampleX * layer.thicknessFreq - elapsedSeconds * layer.thicknessSpeed + layer.seed);
            const thicknessB = Math.cos(sampleX * layer.thicknessFreq * 0.39 + elapsedSeconds * layer.thicknessSpeed * 0.57 + layer.phase);
            const thicknessC = Math.sin(sampleX * layer.freq * 0.09 - elapsedSeconds * 0.21 + layer.phase * 0.8);
            const envelope = 0.78 + thicknessA * layer.thicknessPulse + thicknessB * (layer.thicknessPulse * 0.45) + thicknessC * 0.12;
            const grainBase = organicNoise(sampleX * layer.grainFreq, elapsedSeconds * layer.grainSpeed, layer.seed);
            const grainTop = organicNoise(sampleX * layer.grainFreq * 1.6, elapsedSeconds * layer.grainSpeed * 1.08, layer.seed + 1.4);
            const grainBottom = organicNoise(sampleX * layer.grainFreq * 1.28, elapsedSeconds * layer.grainSpeed * 0.92, layer.seed + 2.2);
            const y = baseY + primary + secondary + tertiary + lift + grainBase * layer.grain * 0.48;
            const thickness = Math.max(layer.band * 0.42, layer.band * envelope);
            const topThickness = Math.max(layer.band * 0.28, thickness * (0.96 + grainTop * 0.08) + grainTop * layer.grain * 0.85);
            const bottomThickness = Math.max(layer.band * 0.18, thickness * 0.68 * (0.98 + grainBottom * 0.1) + grainBottom * layer.grain * 0.55);

            points.push({ x, y, topThickness, bottomThickness });
        }

        return { points, centerY: baseY };
    }

    function drawRibbonFill(ctx, ribbon, layer, state, elapsedSeconds) {
        const { points, centerY } = ribbon;
        if (!points.length) return;
        const highlight = brighten(layer.color, 0.3);
        const core = brighten(layer.color, 0.1);

        ctx.beginPath();

        points.forEach((point, index) => {
            const topY = point.y - point.topThickness;
            if (index === 0) ctx.moveTo(point.x, topY);
            else ctx.lineTo(point.x, topY);
        });

        for (let i = points.length - 1; i >= 0; i--) {
            const point = points[i];
            ctx.lineTo(point.x, point.y + point.bottomThickness);
        }

        ctx.closePath();

        const gradient = ctx.createLinearGradient(0, centerY - layer.band * 1.8, 0, centerY + layer.band * 1.6);
        gradient.addColorStop(0, rgba(layer.color, 0));
        gradient.addColorStop(0.14, rgba(layer.color, layer.fillOpacity * 0.18));
        gradient.addColorStop(0.34, rgba(core, layer.fillOpacity * 0.5));
        gradient.addColorStop(0.5, rgba(highlight, layer.fillOpacity * 0.92));
        gradient.addColorStop(0.64, rgba(core, layer.fillOpacity * 0.42));
        gradient.addColorStop(0.82, rgba(layer.color, layer.fillOpacity * 0.14));
        gradient.addColorStop(1, rgba(layer.color, 0));

        ctx.save();
        ctx.fillStyle = gradient;
        ctx.shadowColor = rgba(layer.color, layer.glowOpacity);
        ctx.shadowBlur = 18;
        ctx.fill();

        if (state.noisePattern) {
            ctx.clip();
            ctx.globalAlpha = layer.textureOpacity;
            ctx.globalCompositeOperation = 'soft-light';
            ctx.translate(
                (elapsedSeconds * layer.speed * 12) % state.noiseTileSize,
                (elapsedSeconds * layer.rise * 4) % state.noiseTileSize
            );
            ctx.fillStyle = state.noisePattern;
            ctx.fillRect(
                -state.noiseTileSize,
                -state.noiseTileSize,
                state.width + state.noiseTileSize * 2,
                state.height + state.noiseTileSize * 2
            );
        }

        ctx.restore();
    }

    function drawRibbonLine(ctx, ribbon, layer) {
        const { points } = ribbon;
        if (!points.length) return;
        const highlight = brighten(layer.color, 0.34);

        ctx.beginPath();

        points.forEach((point, index) => {
            if (index === 0) ctx.moveTo(point.x, point.y);
            else ctx.lineTo(point.x, point.y);
        });

        ctx.save();
        ctx.strokeStyle = rgba(layer.color, layer.edgeOpacity * 0.34);
        ctx.lineWidth = Math.max(3.4, layer.band * 0.12);
        ctx.shadowColor = rgba(layer.color, layer.glowOpacity * 1.05);
        ctx.shadowBlur = 12;
        ctx.stroke();
        ctx.restore();

        ctx.save();
        ctx.strokeStyle = rgba(highlight, layer.edgeOpacity);
        ctx.lineWidth = layer.lineWidth;
        ctx.shadowColor = rgba(layer.color, layer.glowOpacity * 0.8);
        ctx.shadowBlur = 7;
        ctx.stroke();
        ctx.restore();
    }

    function render(state, now) {
        const ctx = state.ctx;
        const width = state.width || 0;
        const height = state.height || 0;

        if (!width || !height) return;

        const elapsedSeconds = state.reducedMotion ? 0 : now * 0.001;

        ctx.clearRect(0, 0, width, height);
        ctx.save();
        ctx.globalCompositeOperation = 'screen';

        WAVE_CONFIGS.forEach(layer => {
            const ribbon = buildLayerPoints(layer, width, height, elapsedSeconds);
            drawRibbonFill(ctx, ribbon, layer, state, elapsedSeconds);
            drawRibbonLine(ctx, ribbon, layer);
        });

        ctx.restore();
    }

    function start(state) {
        if (!state.active || state.reducedMotion || state.raf !== null) return;

        const tick = (now) => {
            if (!state.active) return;

            render(state, now);

            if (!state.reducedMotion) {
                state.raf = requestAnimationFrame(tick);
            } else {
                state.raf = null;
            }
        };

        state.raf = requestAnimationFrame(tick);
    }

    function stop(state) {
        if (state.raf !== null) {
            cancelAnimationFrame(state.raf);
            state.raf = null;
        }
    }

    function init(canvasId) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;

        if (_loops[canvasId]) {
            destroy(canvasId);
        }

        const ctx = canvas.getContext('2d');
        if (!ctx) return;

        const motionQuery = window.matchMedia ? window.matchMedia('(prefers-reduced-motion: reduce)') : null;
        const noiseTileSize = 96;
        const noiseTile = createNoiseTile(noiseTileSize);
        const state = {
            canvas,
            ctx,
            width: 0,
            height: 0,
            active: true,
            reducedMotion: motionQuery ? motionQuery.matches : false,
            observer: null,
            motionQuery,
            motionListener: null,
            raf: null,
            noiseTileSize,
            noisePattern: noiseTile ? ctx.createPattern(noiseTile, 'repeat') : null
        };

        const handleMotionChange = (event) => {
            state.reducedMotion = event.matches;
            stop(state);
            render(state, performance.now());
            start(state);
        };

        state.motionListener = handleMotionChange;

        if (motionQuery) {
            if (typeof motionQuery.addEventListener === 'function') {
                motionQuery.addEventListener('change', handleMotionChange);
            } else if (typeof motionQuery.addListener === 'function') {
                motionQuery.addListener(handleMotionChange);
            }
        }

        const resizeObserver = new ResizeObserver(() => {
            resize(state);
            render(state, performance.now());
        });

        if (canvas.parentElement) {
            resizeObserver.observe(canvas.parentElement);
        }

        state.observer = resizeObserver;
        _loops[canvasId] = state;

        resize(state);
        render(state, performance.now());
        start(state);
    }

    function destroy(canvasId) {
        const state = _loops[canvasId];
        if (!state) return;

        state.active = false;
        stop(state);

        if (state.observer) {
            state.observer.disconnect();
        }

        if (state.motionQuery && state.motionListener) {
            if (typeof state.motionQuery.removeEventListener === 'function') {
                state.motionQuery.removeEventListener('change', state.motionListener);
            } else if (typeof state.motionQuery.removeListener === 'function') {
                state.motionQuery.removeListener(state.motionListener);
            }
        }

        delete _loops[canvasId];
    }

    return { init, destroy };
})();
