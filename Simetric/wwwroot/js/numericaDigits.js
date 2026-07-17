window.NumericaDigits = (function () {
    const _instances = {};
    const DIGIT_SEQUENCE = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "0"];
    const PATTERN = [
        { left: 82, top: 12, scale: 0.14, weight: 670, blur: 0.04, peakOpacity: 0.16, driftX: -10, driftY: -18, rotate: -6, letterSpacing: 1.2, duration: 7400 },
        { left: 74, top: 28, scale: 0.23, weight: 760, blur: 0.06, peakOpacity: 0.23, driftX: 8, driftY: -24, rotate: 4, letterSpacing: -0.8, duration: 7800 },
        { left: 86, top: 47, scale: 0.16, weight: 700, blur: 0.02, peakOpacity: 0.15, driftX: -6, driftY: -20, rotate: -4, letterSpacing: 0.6, duration: 7000 },
        { left: 72, top: 64, scale: 0.27, weight: 780, blur: 0.08, peakOpacity: 0.2, driftX: 10, driftY: -26, rotate: 6, letterSpacing: -1, duration: 8200 },
        { left: 84, top: 83, scale: 0.15, weight: 690, blur: 0.04, peakOpacity: 0.14, driftX: -4, driftY: -16, rotate: -5, letterSpacing: 1, duration: 7200 }
    ];

    function spawnDigit(instance) {
        if (!instance.active || !instance.container) {
            return;
        }

        const bounds = instance.container.getBoundingClientRect();
        if (!bounds.width || !bounds.height) {
            return;
        }

        const slotIndex = instance.spawnIndex % instance.pattern.length;
        const slot = instance.pattern[slotIndex];
        const digitValue = DIGIT_SEQUENCE[instance.digitIndex % DIGIT_SEQUENCE.length];
        const duration = instance.reducedMotion
            ? Math.round(slot.duration * 1.18)
            : slot.duration;
        const digit = document.createElement("span");
        const previousDigit = instance.slotElements.get(slotIndex);
        const previousRemoval = instance.slotRemovals.get(slotIndex);

        if (previousRemoval) {
            window.clearTimeout(previousRemoval);
            instance.slotRemovals.delete(slotIndex);
        }

        previousDigit?.remove();

        digit.className = "numerica-digit";
        digit.textContent = digitValue;
        digit.style.left = `${slot.left}%`;
        digit.style.top = `${slot.top}%`;
        digit.style.fontSize = `${Math.max(56, bounds.width * slot.scale).toFixed(0)}px`;
        digit.style.fontWeight = String(slot.weight);
        digit.style.letterSpacing = `${slot.letterSpacing}px`;
        digit.style.filter = `blur(${instance.reducedMotion ? 0 : slot.blur}px)`;
        digit.style.setProperty("--peak-opacity", `${instance.reducedMotion ? Math.max(0.1, slot.peakOpacity - 0.05) : slot.peakOpacity}`);
        digit.style.setProperty("--drift-x", `${instance.reducedMotion ? 0 : slot.driftX}px`);
        digit.style.setProperty("--drift-y", `${instance.reducedMotion ? -12 : slot.driftY}px`);
        digit.style.setProperty("--rotate", `${instance.reducedMotion ? 0 : slot.rotate}deg`);
        digit.style.animation = `numericaDigitFloat ${duration}ms ease-in-out forwards`;

        instance.container.appendChild(digit);
        instance.slotElements.set(slotIndex, digit);

        const removalId = window.setTimeout(() => {
            digit.remove();
            instance.slotElements.delete(slotIndex);
            instance.slotRemovals.delete(slotIndex);
        }, duration);

        instance.slotRemovals.set(slotIndex, removalId);
        instance.spawnIndex += 1;
        instance.digitIndex += 1;
    }

    function scheduleSpawn(containerId) {
        const instance = _instances[containerId];
        if (!instance || !instance.active) {
            return;
        }

        spawnDigit(instance);

        const delay = instance.delays[instance.spawnIndex % instance.delays.length];
        instance.spawnTimer = window.setTimeout(() => scheduleSpawn(containerId), delay);
    }

    function init(containerId) {
        const container = document.getElementById(containerId);
        if (!container) {
            return;
        }

        destroy(containerId);

        const reducedMotion = window.matchMedia
            ? window.matchMedia("(prefers-reduced-motion: reduce)").matches
            : false;

        const instance = {
            active: true,
            container,
            reducedMotion,
            pattern: PATTERN,
            delays: reducedMotion ? [2400, 2600, 2500, 2700, 2400] : [1500, 1700, 1600, 1800, 1650],
            spawnIndex: 0,
            digitIndex: 0,
            spawnTimer: null,
            slotElements: new Map(),
            slotRemovals: new Map()
        };

        _instances[containerId] = instance;

        const initialDigits = reducedMotion ? 2 : 3;
        for (let i = 0; i < initialDigits; i += 1) {
            window.setTimeout(() => {
                if (_instances[containerId]?.active) {
                    spawnDigit(instance);
                }
            }, i * 520);
        }

        instance.spawnTimer = window.setTimeout(
            () => scheduleSpawn(containerId),
            initialDigits * 520 + (reducedMotion ? 950 : 1100)
        );
    }

    function destroy(containerId) {
        const instance = _instances[containerId];
        if (!instance) {
            return;
        }

        instance.active = false;

        if (instance.spawnTimer !== null) {
            window.clearTimeout(instance.spawnTimer);
        }

        instance.slotRemovals.forEach(timeoutId => window.clearTimeout(timeoutId));
        instance.slotRemovals.clear();
        instance.slotElements.clear();
        instance.container?.replaceChildren();

        delete _instances[containerId];
    }

    return { init, destroy };
})();
