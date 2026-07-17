window.numericaCarteraCharts = (function () {
    const charts = {};

    function destroy(key) {
        if (charts[key]) {
            charts[key].destroy();
            delete charts[key];
        }
    }

    function getCanvas(id) {
        const element = document.getElementById(id);
        return element instanceof HTMLCanvasElement ? element : null;
    }

    function renderDoughnut(payload) {
        const canvas = getCanvas("carteraResumenChart");
        if (!canvas || typeof Chart === "undefined") {
            return;
        }

        destroy("resumen");

        charts.resumen = new Chart(canvas, {
            type: "doughnut",
            data: {
                labels: payload.labels || [],
                datasets: [{
                    data: payload.values || [],
                    backgroundColor: payload.colors || [],
                    borderWidth: 0,
                    hoverOffset: 6
                }]
            },
            options: {
                maintainAspectRatio: false,
                cutout: "68%",
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        callbacks: {
                            label(context) {
                                const value = Number(context.raw || 0).toLocaleString("es-EC", {
                                    style: "currency",
                                    currency: "USD",
                                    minimumFractionDigits: 2
                                });
                                return `${context.label}: ${value}`;
                            }
                        }
                    }
                }
            },
            plugins: [{
                id: "centerText",
                afterDraw(chart) {
                    const meta = chart.getDatasetMeta(0);
                    if (!meta?.data?.length) {
                        return;
                    }

                    const { ctx } = chart;
                    const x = meta.data[0].x;
                    const y = meta.data[0].y;
                    const total = (payload.values || []).reduce((sum, value) => sum + Number(value || 0), 0);
                    const totalLabel = total.toLocaleString("es-EC", {
                        style: "currency",
                        currency: "USD",
                        minimumFractionDigits: 2
                    });

                    ctx.save();
                    ctx.textAlign = "center";
                    ctx.fillStyle = "#17324a";
                    ctx.font = "700 18px system-ui";
                    ctx.fillText(totalLabel, x, y - 4);
                    ctx.fillStyle = "#7a8ea1";
                    ctx.font = "600 12px system-ui";
                    ctx.fillText("Total", x, y + 16);
                    ctx.restore();
                }
            }]
        });
    }

    function renderBars(id, key, payload, horizontal) {
        const canvas = getCanvas(id);
        if (!canvas || typeof Chart === "undefined") {
            return;
        }

        destroy(key);

        charts[key] = new Chart(canvas, {
            type: "bar",
            data: {
                labels: payload.labels || [],
                datasets: [{
                    data: payload.values || [],
                    backgroundColor: payload.colors || [],
                    borderRadius: 12,
                    borderSkipped: false,
                    maxBarThickness: horizontal ? 18 : 36
                }]
            },
            options: {
                maintainAspectRatio: false,
                indexAxis: horizontal ? "y" : "x",
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        callbacks: {
                            label(context) {
                                return Number(context.raw || 0).toLocaleString("es-EC", {
                                    style: "currency",
                                    currency: "USD",
                                    minimumFractionDigits: 2
                                });
                            }
                        }
                    }
                },
                scales: {
                    x: {
                        grid: { color: "rgba(15, 35, 60, 0.08)" },
                        ticks: {
                            color: "#6a8093",
                            callback(value) {
                                return Number(value || 0).toLocaleString("es-EC", {
                                    style: "currency",
                                    currency: "USD",
                                    minimumFractionDigits: 0
                                });
                            }
                        }
                    },
                    y: {
                        grid: { display: !horizontal, color: "rgba(15, 35, 60, 0.08)" },
                        ticks: { color: "#4a6075" }
                    }
                }
            }
        });
    }

    function render(payload) {
        renderDoughnut(payload.resumen || {});
        renderBars("carteraAntiguedadChart", "antiguedad", payload.antiguedad || {}, false);
        renderBars("carteraTopClientesChart", "topClientes", payload.topClientes || {}, true);
    }

    return {
        render
    };
})();
