window.authInterop = {
    login: function (payload) {
        return this.postJson("/api/auth/login", payload);
    },
    check: async function () {
        try {
            const response = await fetch("/api/auth/check", {
                method: "GET",
                credentials: "include"
            });
            return await response.json();
        } catch {
            return { authenticated: false, idUsuario: 0 };
        }
    },
    mfaLogin: function (payload) {
        return this.postJson("/api/auth/mfa-login", payload);
    },

    logout: async function () {
        try {
            return await this.postJson("/api/auth/logout", {});
        } finally {
            this.clearClientSession();
        }
    },

    logoutAndRedirect: async function (url) {
        try {
            await this.logout();
        } finally {
            window.location.replace(url || "/login");
        }
    },

    clearClientSession: function () {
        try {
            localStorage.removeItem("userSession");
            localStorage.removeItem("numerica:last-activity");
            localStorage.removeItem("numerica:current-service");
            localStorage.removeItem("selectedAppService");
            sessionStorage.removeItem("numerica:session-expired");
        } catch {
            // Ignorado a proposito.
        }
    },

    postJson: async function (url, payload) {
        const response = await fetch(url, {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            credentials: "include",
            body: JSON.stringify(payload)
        });

        const text = await response.text();

        if (!text) {
            return {
                ok: response.ok,
                status: response.status,
                statusText: response.statusText,
                message: response.ok ? "" : `HTTP ${response.status} ${response.statusText}`
            };
        }

        try {
            const data = JSON.parse(text);
            return {
                ok: response.ok,
                status: response.status,
                statusText: response.statusText,
                ...data
            };
        } catch {
            return {
                ok: response.ok,
                status: response.status,
                statusText: response.statusText,
                message: text
            };
        }
    }
};

(function initializeInactivityMonitor() {
    const SESSION_KEY = "userSession";
    const LAST_ACTIVITY_KEY = "numerica:last-activity";
    const SESSION_EXPIRED_KEY = "numerica:session-expired";
    const INACTIVITY_TIMEOUT_MS = 30 * 60 * 1000;
    const ACTIVITY_WRITE_THROTTLE_MS = 15 * 1000;
    const CHECK_INTERVAL_MS = 60 * 1000;
    const ACTIVITY_EVENTS = ["click", "keydown", "mousedown", "scroll", "touchstart", "mousemove"];

    let lastPersistedActivity = 0;
    let logoutInProgress = false;

    function hasSession() {
        try {
            return !!localStorage.getItem(SESSION_KEY);
        } catch {
            return false;
        }
    }

    function persistActivity(force) {
        if (!hasSession()) {
            return;
        }

        const now = Date.now();
        if (!force && (now - lastPersistedActivity) < ACTIVITY_WRITE_THROTTLE_MS) {
            return;
        }

        lastPersistedActivity = now;
        localStorage.setItem(LAST_ACTIVITY_KEY, now.toString());
    }

    async function logoutDueToInactivity() {
        if (logoutInProgress) {
            return;
        }

        logoutInProgress = true;

        try {
            sessionStorage.setItem(SESSION_EXPIRED_KEY, "1");
        } catch {
            // Ignorado a proposito.
        }

        try {
            await window.authInterop.logout();
        } catch {
            window.authInterop.clearClientSession();
        }

        if (!window.location.pathname.startsWith("/login")) {
            window.location.replace("/login");
        }
    }

    async function checkInactivity() {
        if (!hasSession()) {
            return;
        }

        const rawLastActivity = localStorage.getItem(LAST_ACTIVITY_KEY);
        const lastActivity = Number.parseInt(rawLastActivity || "0", 10);

        if (!Number.isFinite(lastActivity) || lastActivity <= 0) {
            persistActivity(true);
            return;
        }

        if ((Date.now() - lastActivity) >= INACTIVITY_TIMEOUT_MS) {
            await logoutDueToInactivity();
        }
    }

    ACTIVITY_EVENTS.forEach(eventName => {
        window.addEventListener(eventName, function () {
            persistActivity(false);
        }, { passive: true });
    });

    window.addEventListener("focus", function () {
        checkInactivity();
    });

    document.addEventListener("visibilitychange", function () {
        if (document.visibilityState === "visible") {
            checkInactivity();
        }
    });

    window.addEventListener("storage", function (event) {
        if (event.key === LAST_ACTIVITY_KEY && hasSession()) {
            lastPersistedActivity = Date.now();
        }

        if (event.key === SESSION_KEY && !event.newValue) {
            window.authInterop.clearClientSession();
        }
    });

    if (hasSession()) {
        persistActivity(true);
    }

    window.setInterval(checkInactivity, CHECK_INTERVAL_MS);
    checkInactivity();
})();
