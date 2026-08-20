export function portalOverlayToBody(elementId) {
    const overlay = document.getElementById(elementId);

    if (!overlay || overlay.parentElement === document.body) {
        return;
    }

    document.body.appendChild(overlay);
}
