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
        if (!digits) return "";

        const guide = [...(includesTime ? "dd/mm/aaaa hh:mm" : "dd/mm/aaaa")];
        const positions = includesTime
            ? [0, 1, 3, 4, 6, 7, 8, 9, 11, 12, 14, 15]
            : [0, 1, 3, 4, 6, 7, 8, 9];

        [...digits].forEach((digit, index) => {
            guide[positions[index]] = digit;
        });

        return guide.join("");
    }

    function placeCaretAfterLastDigit(input, includesTime) {
        const digitCount = input.value.replace(/\D/g, "").length;
        const positions = includesTime
            ? [0, 1, 3, 4, 6, 7, 8, 9, 11, 12, 14, 15]
            : [0, 1, 3, 4, 6, 7, 8, 9];
        const caretPosition = digitCount > 0 ? positions[Math.min(digitCount, positions.length) - 1] + 1 : 0;
        input.setSelectionRange(caretPosition, caretPosition);
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
        visible.title = includesTime ? "Formato: dd/mm/aaaa hh:mm" : "Formato: dd/mm/aaaa";
        visible.inputMode = "numeric";
        visible.autocomplete = "off";
        visible.maxLength = includesTime ? 24 : 18;
        visible.value = formatValue(input.value, includesTime);
        visible.disabled = input.disabled;
        visible.readOnly = input.readOnly;
        visible.required = input.required;
        visible.setAttribute("aria-label", input.getAttribute("aria-label") || visible.placeholder);
        visible.dataset.dateDisplayFor = "true";
        visible.classList.add("localized-date-display");

        const pickerButton = document.createElement("button");
        pickerButton.type = "button";
        pickerButton.className = "localized-date-picker";
        pickerButton.setAttribute("aria-label", includesTime ? "Abrir calendario y hora" : "Abrir calendario");
        pickerButton.innerHTML = '<i class="bi bi-calendar3" aria-hidden="true"></i>';

        input.dataset.localizedDate = "true";
        input.dataset.localizedDateNative = "true";
        input.lang = "es-EC";
        input.classList.add("localized-native-date");
        input.style.setProperty("position", "absolute", "important");
        input.style.setProperty("width", "1px", "important");
        input.style.setProperty("height", "1px", "important");
        input.style.setProperty("padding", "0", "important");
        input.style.setProperty("margin", "0", "important");
        input.style.setProperty("opacity", "0", "important");
        input.style.setProperty("pointer-events", "none", "important");
        input.setAttribute("aria-hidden", "true");
        input.tabIndex = -1;
        input.before(visible);
        visible.after(pickerButton);

        const synchronizeState = () => {
            visible.disabled = input.disabled;
            visible.readOnly = input.readOnly;
            visible.required = input.required;
            pickerButton.disabled = input.disabled || input.readOnly;
        };

        pickerButton.addEventListener("click", () => {
            if (input.disabled || input.readOnly) return;

            if (typeof input.showPicker === "function") {
                try {
                    input.showPicker();
                    return;
                } catch {
                    // Algunos navegadores no permiten abrir el selector desde un control oculto.
                }
            }

            input.click();
        });

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
            placeCaretAfterLastDigit(visible, includesTime);
        });
        visible.addEventListener("keydown", event => {
            if (event.key !== "Backspace" && event.key !== "Delete") return;

            event.preventDefault();
            const digits = visible.value.replace(/\D/g, "").slice(0, -1);
            visible.value = maskValue(digits, includesTime);
            visible.setCustomValidity("");
            placeCaretAfterLastDigit(visible, includesTime);
        });
        visible.addEventListener("change", commit);
        visible.addEventListener("blur", () => {
            commit();
            input.dispatchEvent(new Event("blur"));
        });
        input.addEventListener("input", () => {
            visible.value = formatValue(input.value, includesTime);
            synchronizeState();
        });
        input.addEventListener("change", () => {
            visible.value = formatValue(input.value, includesTime);
            synchronizeState();
        });

        synchronizeState();
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
