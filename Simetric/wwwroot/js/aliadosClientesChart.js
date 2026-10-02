window.aliadosClientesChart = (function () {
    let chart;

    function destroy() {
        if (chart) {
            chart.destroy();
            chart = null;
        }
    }

    function render(payload) {
        if (!window.Chart || !payload) return;
        const canvas = document.getElementById("ally-clients-purchases-chart");
        if (!canvas) return;
        destroy();

        const values = payload.values || [];
        const empty = values.length === 0;
        const aliadosPalette = ["#7b1e3a", "#9f1747", "#b82e61", "#cf4d78", "#df7897"];
        chart = new Chart(canvas, {
            type: "bar",
            data: {
                labels: empty ? ["Sin compras"] : payload.labels,
                datasets: [{
                    label: "Compras",
                    data: empty ? [0] : values,
                    backgroundColor: empty ? "#ead8df" : values.map((_, index) => aliadosPalette[index % aliadosPalette.length]),
                    hoverBackgroundColor: empty ? "#dec4ce" : values.map(() => "#64162f"),
                    borderRadius: 6,
                    borderSkipped: false,
                    maxBarThickness: 32
                }]
            },
            options: {
                indexAxis: "y",
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: { display: false },
                    tooltip: { callbacks: { label: context => `${context.parsed.x || 0} compra(s)` } }
                },
                scales: {
                    x: { beginAtZero: true, ticks: { precision: 0, color: "#71869d" }, grid: { color: "rgba(112,135,159,.14)" } },
                    y: { ticks: { color: "#38536f" }, grid: { display: false } }
                }
            }
        });
    }

    return { render, destroy };
})();
