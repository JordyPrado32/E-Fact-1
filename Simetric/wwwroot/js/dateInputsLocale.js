(function () {
    "use strict";

    const selector = 'input[type="date"], input[type="datetime-local"]';

    function formatValue(value, includesTime) {
        const match = value?.match(/^(\d{4})-(\d{2})-(\d{2})(?:T(\d{2}):(\d{2}))?/);
        if (!match) return "";

        const date = `${match[3]}/${match[2]}/${match[1]}`;
        return includesTime && match[4] ? `${date} ${match[4]}:${match[5]}` : date;
    }

    function maskValue(value, includesTime) {
        const digits = value.replace(/\D/g, "").slice(0, includesTime ? 12 : 8);
        const parts = [digits.slice(0, 2), digits.slice(2, 4), digits.slice(4, 8)];
        let result = parts.filter(Boolean).join("/");

        if (includesTime && digits.length > 8) {
            const time = [digits.slice(8, 10), digits.slice(10, 12)].filter(Boolean).join(":");
            result += ` ${time}`;
        }

        return result;
    }

    function parseValue(value, includesTime) {
        const pattern = includesTime
            ? /^(\d{2})\/(\d{2})\/(\d{4})\s+(\d{2}):(\d{2})$/
            : /^(\d{2})\/(\d{2})\/(\d{4})$/;
        const match = value.trim().match(pattern);
        if (!match) return null;

        const day = Number(match[1]);
        const month = Number(match[2]);
        const year = Number(match[3]);
        const hour = includesTime ? Number(match[4]) : 0;
        const minute = includesTime ? Number(match[5]) : 0;
        const date = new Date(year, month - 1, day, hour, minute);

        if (date.getFullYear() !== year || date.getMonth() !== month - 1 || date.getDate() !== day ||
            date.getHours() !== hour || date.getMinutes() !== minute) {
            return null;
        }

        const isoDate = `${match[3]}-${match[2]}-${match[1]}`;
        return includesTime ? `${isoDate}T${match[4]}:${match[5]}` : isoDate;
    }

    function enhance(input) {
        if (input.dataset.localizedDate === "true") return;

        const includesTime = input.type === "datetime-local";
        const visible = input.cloneNode(false);
        visible.type = "text";
        visible.removeAttribute("id");
        visible.removeAttribute("name");
        visible.removeAttribute("min");
        visible.removeAttribute("max");
        visible.removeAttribute("step");
        visible.removeAttribute("data-localized-date");
        visible.placeholder = includesTime ? "dd/mm/aaaa hh:mm" : "dd/mm/aaaa";
        visible.inputMode = "numeric";
        visible.autocomplete = "off";
        visible.maxLength = includesTime ? 16 : 10;
        visible.value = formatValue(input.value, includesTime);
        visible.disabled = input.disabled;
        visible.readOnly = input.readOnly;
        visible.required = input.required;
        visible.setAttribute("aria-label", input.getAttribute("aria-label") || visible.placeholder);
        visible.dataset.dateDisplayFor = "true";

        input.dataset.localizedDate = "true";
        input.lang = "es-EC";
        input.classList.add("localized-native-date");
        input.setAttribute("aria-hidden", "true");
        input.tabIndex = -1;
        input.before(visible);

        const synchronizeState = () => {
            visible.disabled = input.disabled;
            visible.readOnly = input.readOnly;
            visible.required = input.required;
        };

        const commit = () => {
            visible.setCustomValidity("");
            const text = visible.value.trim();

            if (!text) {
                if (input.value) {
                    input.value = "";
                    input.dispatchEvent(new Event("input", { bubbles: true }));
                    input.dispatchEvent(new Event("change", { bubbles: true }));
                }
                return;
            }

            const isoValue = parseValue(text, includesTime);
            const outsideRange = isoValue &&
                ((input.min && isoValue < input.min) || (input.max && isoValue > input.max));

            if (!isoValue || outsideRange) {
                visible.setCustomValidity(includesTime
                    ? "Ingrese una fecha y hora válidas en formato día/mes/año hora:minuto."
                    : "Ingrese una fecha válida en formato día/mes/año.");
                visible.reportValidity();
                return;
            }

            visible.value = formatValue(isoValue, includesTime);
            if (input.value !== isoValue) {
                input.value = isoValue;
                input.dispatchEvent(new Event("input", { bubbles: true }));
                input.dispatchEvent(new Event("change", { bubbles: true }));
            }
        };

        visible.addEventListener("input", () => {
            visible.setCustomValidity("");
            visible.value = maskValue(visible.value, includesTime);
        });
        visible.addEventListener("change", commit);
        visible.addEventListener("blur", commit);
        input.addEventListener("input", () => {
            visible.value = formatValue(input.value, includesTime);
            synchronizeState();
        });
        input.addEventListener("change", () => {
            visible.value = formatValue(input.value, includesTime);
            synchronizeState();
        });
    }

    function apply(root) {
        if (root instanceof HTMLInputElement && root.matches(selector)) enhance(root);
        root.querySelectorAll?.(selector).forEach(enhance);
    }

    function initialize() {
        apply(document);
        new MutationObserver(mutations => {
            mutations.forEach(mutation => mutation.addedNodes.forEach(node => {
                if (node instanceof Element) apply(node);
            }));
        }).observe(document.body, { childList: true, subtree: true });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, { once: true });
    } else {
        initialize();
    }
})();
