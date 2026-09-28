window.numericaClipboard = window.numericaClipboard || {};

window.numericaClipboard.copy = async function (text) {
    if (!text) {
        return false;
    }

    try {
        if (navigator.clipboard && window.isSecureContext) {
            await navigator.clipboard.writeText(text);
            return true;
        }
    } catch {
        // Algunos navegadores bloquean Clipboard API aunque la página use HTTPS.
    }

    const input = document.createElement('textarea');
    input.value = text;
    input.setAttribute('readonly', '');
    input.style.cssText = 'position:fixed;left:-9999px;top:0;opacity:0;';
    document.body.appendChild(input);
    input.select();
    input.setSelectionRange(0, input.value.length);

    try {
        return document.execCommand('copy');
    } finally {
        document.body.removeChild(input);
    }
};
