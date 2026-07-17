(function () {
    const charts = {};

    function renderProgress(canvasId, completed, pending) {
        const canvas = document.getElementById(canvasId);

        if (!canvas || !window.Chart) {
            return;
        }

        const completedValue = Math.max(Number(completed) || 0, 0);
        const pendingValue = Math.max(Number(pending) || 0, 0);
        const hasData = completedValue + pendingValue > 0;

        if (charts[canvasId]) {
            charts[canvasId].destroy();
        }

        charts[canvasId] = new window.Chart(canvas, {
            type: "doughnut",
            data: {
                labels: ["Completados", "Pendientes"],
                datasets: [{
                    data: hasData ? [completedValue, pendingValue] : [1, 0],
                    backgroundColor: ["#2bbf7a", "#f2a20c"],
                    borderColor: "#ffffff",
                    borderWidth: 4,
                    borderRadius: 10,
                    hoverOffset: 0,
                    spacing: 2
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                cutout: "72%",
                events: [],
                animation: {
                    duration: 650,
                    easing: "easeOutQuart"
                },
                plugins: {
                    legend: {
                        display: false
                    },
                    tooltip: {
                        enabled: false
                    }
                }
            }
        });
    }

    window.tutorialCharts = window.tutorialCharts || {};
    window.tutorialCharts.renderProgress = renderProgress;
})();
