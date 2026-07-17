window.numericaDashboardCharts = (function () {
    const charts = {};
    const currencyFormatter = new Intl.NumberFormat("es-EC", {
        style: "currency",
        currency: "USD",
        minimumFractionDigits: 2
    });
    const numberFormatter = new Intl.NumberFormat("es-EC");

    function toNumbers(values) {
        return Array.isArray(values)
            ? values.map(value => Number(value || 0))
            : [];
    }

    function hasPositiveValue(values) {
        return toNumbers(values).some(value => value > 0);
    }

    function destroyChart(key) {
        if (charts[key]) {
            charts[key].destroy();
            delete charts[key];
        }
    }

    function getCanvas(id) {
        const canvas = document.getElementById(id);
        return canvas instanceof HTMLCanvasElement ? canvas : null;
    }

    function getTooltipElement() {
        let tooltipEl = document.getElementById("numerica-chart-tooltip");

        if (!tooltipEl) {
            tooltipEl = document.createElement("div");
            tooltipEl.id = "numerica-chart-tooltip";
            tooltipEl.style.position = "fixed";
            tooltipEl.style.zIndex = "99999";
            tooltipEl.style.pointerEvents = "none";
            tooltipEl.style.opacity = "0";
            tooltipEl.style.display = "none";
            tooltipEl.style.maxWidth = "260px";
            tooltipEl.style.padding = "10px 12px";
            tooltipEl.style.border = "1px solid rgba(11, 91, 151, 0.18)";
            tooltipEl.style.borderRadius = "14px";
            tooltipEl.style.background = "rgba(255, 255, 255, 0.98)";
            tooltipEl.style.boxShadow = "0 18px 42px rgba(15, 23, 42, 0.18)";
            tooltipEl.style.backdropFilter = "blur(10px)";
            tooltipEl.style.color = "#17324a";
            tooltipEl.style.fontFamily = "inherit";
            tooltipEl.style.transition = "opacity 0.12s ease, transform 0.12s ease";
            document.body.appendChild(tooltipEl);
        }

        return tooltipEl;
    }

    function setTooltipContent(tooltipEl, tooltip) {
        tooltipEl.replaceChildren();

        const titleLines = tooltip.title || [];
        if (titleLines.length > 0) {
            const title = document.createElement("div");
            title.style.marginBottom = "7px";
            title.style.color = "#0b5b97";
            title.style.fontSize = "12px";
            title.style.fontWeight = "800";
            title.style.lineHeight = "1.25";
            title.textContent = titleLines.join(" ");
            tooltipEl.appendChild(title);
        }

        const bodyLines = tooltip.body ? tooltip.body.flatMap(item => item.lines || []) : [];
        bodyLines.forEach((line, index) => {
            const row = document.createElement("div");
            row.style.display = "grid";
            row.style.gridTemplateColumns = "auto minmax(0, 1fr)";
            row.style.alignItems = "center";
            row.style.gap = "8px";
            row.style.marginTop = index === 0 ? "0" : "5px";

            const swatch = document.createElement("span");
            swatch.style.width = "10px";
            swatch.style.height = "10px";
            swatch.style.borderRadius = "999px";
            swatch.style.background = tooltip.labelColors?.[index]?.backgroundColor || "#0b5b97";
            swatch.style.boxShadow = "0 0 0 4px rgba(0, 107, 181, 0.09)";

            const text = document.createElement("span");
            text.style.minWidth = "0";
            text.style.color = "#17324a";
            text.style.fontSize = "12px";
            text.style.fontWeight = "700";
            text.style.lineHeight = "1.35";
            text.textContent = line;

            row.appendChild(swatch);
            row.appendChild(text);
            tooltipEl.appendChild(row);
        });
    }

    function externalTooltipHandler(context) {
        const { chart, tooltip } = context;
        const tooltipEl = getTooltipElement();

        if (!tooltip || tooltip.opacity === 0) {
            tooltipEl.style.opacity = "0";
            tooltipEl.style.display = "none";
            return;
        }

        setTooltipContent(tooltipEl, tooltip);

        const rect = chart.canvas.getBoundingClientRect();
        const rawLeft = rect.left + tooltip.caretX;
        const rawTop = rect.top + tooltip.caretY;

        tooltipEl.style.display = "block";
        tooltipEl.style.opacity = "1";
        tooltipEl.style.transform = "translate(-50%, calc(-100% - 12px))";

        const tooltipRect = tooltipEl.getBoundingClientRect();
        const safeLeft = Math.min(
            Math.max(rawLeft, 14 + tooltipRect.width / 2),
            window.innerWidth - 14 - tooltipRect.width / 2);
        const safeTop = Math.max(rawTop, 18 + tooltipRect.height);

        tooltipEl.style.left = `${safeLeft}px`;
        tooltipEl.style.top = `${safeTop}px`;
    }

    function renderLineChart(payload) {
        const canvas = getCanvas("dashboardSalesChart");
        destroyChart("sales");

        if (!canvas || !payload || !hasPositiveValue(payload.current) && !hasPositiveValue(payload.previous)) {
            return;
        }

        const ctx = canvas.getContext("2d");
        const gradient = ctx.createLinearGradient(0, 0, 0, 240);
        gradient.addColorStop(0, "rgba(11, 91, 151, 0.32)");
        gradient.addColorStop(0.5, "rgba(11, 91, 151, 0.12)");
        gradient.addColorStop(1, "rgba(11, 91, 151, 0.0)");

        charts.sales = new Chart(canvas, {
            type: "line",
            data: {
                labels: payload.labels || [],
                datasets: [
                    {
                        label: payload.currentLabel || "Actual",
                        data: toNumbers(payload.current),
                        borderColor: "#0b5b97",
                        backgroundColor: gradient,
                        pointBackgroundColor: "#0b5b97",
                        pointBorderColor: "#ffffff",
                        pointBorderWidth: 2,
                        pointRadius: 4,
                        pointHoverRadius: 6,
                        tension: 0.38,
                        fill: true
                    },
                    {
                        label: payload.previousLabel || "Anterior",
                        data: toNumbers(payload.previous),
                        borderColor: "#94a3b8",
                        backgroundColor: "rgba(148, 163, 184, 0.08)",
                        pointBackgroundColor: "#94a3b8",
                        pointBorderColor: "#ffffff",
                        pointBorderWidth: 2,
                        pointRadius: 3,
                        tension: 0.38,
                        borderDash: [6, 6],
                        fill: false
                    }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                interaction: { intersect: false, mode: "index" },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        enabled: false,
                        external: externalTooltipHandler,
                        callbacks: {
                            label: context => `${context.dataset.label}: ${currencyFormatter.format(context.parsed.y || 0)}`
                        }
                    }
                },
                scales: {
                    x: {
                        grid: { display: false },
                        ticks: { color: "#60768b", font: { size: 11, weight: "700" } }
                    },
                    y: {
                        beginAtZero: true,
                        border: { display: false },
                        grid: { color: "rgba(148, 163, 184, 0.22)" },
                        ticks: {
                            color: "#60768b",
                            callback: value => currencyFormatter.format(Number(value || 0)).replace(",00", "")
                        }
                    }
                }
            }
        });
    }

    function renderESignLineChart(payload) {
        const canvas = getCanvas("esignRequestsChart");
        destroyChart("esignRequests");

        if (!canvas || !payload || !hasPositiveValue(payload.values)) {
            return;
        }

        const ctx = canvas.getContext("2d");
        const gradient = ctx.createLinearGradient(0, 0, 0, 280);
        gradient.addColorStop(0, "rgba(46, 125, 50, 0.28)");
        gradient.addColorStop(0.55, "rgba(46, 125, 50, 0.10)");
        gradient.addColorStop(1, "rgba(46, 125, 50, 0)");

        charts.esignRequests = new Chart(canvas, {
            type: "line",
            data: {
                labels: payload.labels || [],
                datasets: [{
                    label: "Solicitudes",
                    data: toNumbers(payload.values),
                    borderColor: "#2e7d32",
                    backgroundColor: gradient,
                    pointBackgroundColor: "#2e7d32",
                    pointBorderColor: "#ffffff",
                    pointBorderWidth: 2,
                    pointRadius: 4,
                    pointHoverRadius: 6,
                    tension: 0.35,
                    fill: true
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                interaction: { intersect: false, mode: "index" },
                layout: { padding: 2 },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        enabled: false,
                        external: externalTooltipHandler,
                        callbacks: {
                            label: context => `${numberFormatter.format(context.parsed.y || 0)} solicitud(es)`
                        }
                    }
                },
                scales: {
                    x: {
                        display: false,
                        grid: { display: false }
                    },
                    y: {
                        display: false,
                        beginAtZero: true,
                        border: { display: false },
                        grid: { display: false }
                    }
                }
            }
        });
    }

    function renderDoughnutChart(key, canvasId, payload, colors, tooltipFormatter) {
        const canvas = getCanvas(canvasId);
        destroyChart(key);

        if (!canvas || !payload) {
            return;
        }

        const values = toNumbers(payload.values);
        const hasData = values.some(v => v > 0);

        const chartData = hasData ? values : [1];
        const chartColors = hasData ? colors : ["#e2e8f0"];
        const chartLabels = hasData ? (payload.labels || []) : ["Sin registros"];

        charts[key] = new Chart(canvas, {
            type: "doughnut",
            data: {
                labels: chartLabels,
                datasets: [{
                    data: chartData,
                    backgroundColor: chartColors,
                    borderWidth: 0,
                    hoverOffset: hasData ? 5 : 0
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                cutout: "72%",
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        enabled: false,
                        external: externalTooltipHandler,
                        callbacks: {
                            label: context => {
                                const label = context.label || "";
                                const value = context.parsed || 0;
                                return `${label}: ${tooltipFormatter(value)}`;
                            }
                        }
                    }
                }
            }
        });
    }


    function renderHorizontalBarChart(key, canvasId, payload) {
        const canvas = getCanvas(canvasId);
        destroyChart(key);

        if (!canvas || !payload || !hasPositiveValue(payload.values)) {
            return;
        }

        const values = toNumbers(payload.values);
        const totals = toNumbers(payload.totals);
        const colors = Array.isArray(payload.colors) && payload.colors.length > 0
            ? payload.colors
            : ["#f59e0b", "#10b981", "#8b5cf6", "#f97316", "#ec4899"];

        charts[key] = new Chart(canvas, {
            type: "bar",
            data: {
                labels: payload.labels || [],
                datasets: [{
                    data: values,
                    backgroundColor: colors,
                    borderRadius: 999,
                    borderSkipped: false,
                    barThickness: 12,
                    maxBarThickness: 14
                }]
            },
            options: {
                indexAxis: "y",
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        enabled: false,
                        external: externalTooltipHandler,
                        callbacks: {
                            label: context => {
                                const count = context.parsed.x || 0;
                                const total = totals[context.dataIndex] || 0;
                                return `${numberFormatter.format(count)} doc. · ${currencyFormatter.format(total)}`;
                            }
                        }
                    }
                },
                scales: {
                    x: {
                        beginAtZero: true,
                        border: { display: false },
                        grid: { color: "rgba(148, 163, 184, 0.2)" },
                        ticks: {
                            color: "#60768b",
                            precision: 0,
                            font: { size: 10, weight: "700" }
                        }
                    },
                    y: {
                        border: { display: false },
                        grid: { display: false },
                        ticks: {
                            color: "#17324a",
                            font: { size: 10, weight: "700" },
                            callback: function (value) {
                                const label = this.getLabelForValue(value) || "";
                                return label.length > 26 ? `${label.slice(0, 24)}...` : label;
                            }
                        }
                    }
                }
            }
        });
    }

    function render(payload) {
        if (!window.Chart) {
            return;
        }

        renderLineChart(payload && payload.sales);

        renderDoughnutChart(
            "collection",
            "dashboardCollectionChart",
            payload && payload.collection,
            ["#0b5b97", "#f59e0b", "#10b981"],
            value => currencyFormatter.format(value));

        const documentColors = payload && payload.documents && Array.isArray(payload.documents.colors)
            ? payload.documents.colors
            : ["#14b8a6", "#8b5cf6", "#f97316", "#ec4899", "#0ea5e9", "#10b981"];

        renderDoughnutChart(
            "documents",
            "dashboardDocumentChart",
            payload && payload.documents,
            documentColors,
            value => `${value} documento(s)`);
    }

    function renderReportDocuments(payload) {
        if (!window.Chart) {
            return;
        }

        const documentColors = payload && payload.documentTypes && Array.isArray(payload.documentTypes.colors)
            ? payload.documentTypes.colors
            : ["#14b8a6", "#10b981", "#f59e0b", "#8b5cf6", "#f97316", "#ec4899"];

        renderDoughnutChart(
            "reportDocumentTypes",
            "reportDocumentTypeChart",
            payload && payload.documentTypes,
            documentColors,
            value => `${numberFormatter.format(value)} doc.`);

        renderHorizontalBarChart(
            "reportTopThirdParties",
            "reportTopThirdPartiesChart",
            payload && payload.topThirdParties);
    }

    function destroyReportDocuments() {
        destroyChart("reportDocumentTypes");
        destroyChart("reportTopThirdParties");
    }

    function renderSubscriptions(efactPayload, esignPayload) {
        if (!window.Chart) {
            return;
        }

        renderDoughnutChart(
            "efact",
            "efactDoughnutChart",
            efactPayload,
            ["#10b981", "#f59e0b", "#ef4444"],
            value => `${value} cliente(s)`);

        renderDoughnutChart(
            "esign",
            "esignDoughnutChart",
            esignPayload,
            ["#10b981", "#f59e0b", "#ef4444"],
            value => `${value} firma(s)`);
    }

    function renderESign(payload) {
        if (!window.Chart) {
            return;
        }

        renderESignLineChart(payload && payload.requests);

        renderDoughnutChart(
            "esignStatus",
            "esignStatusChart",
            payload && payload.status,
            ["#22c55e", "#3b82f6", "#f59e0b", "#ef4444"],
            value => `${numberFormatter.format(value)} solicitud(es)`);

        destroyChart("esignFormat");
    }

    function scrollToDetails() {
        const el = document.querySelector(".details-section");
        if (el) {
            el.scrollIntoView({ behavior: "smooth", block: "start" });
        }
    }

    function destroy() {
        Object.keys(charts).forEach(destroyChart);
        const tooltipEl = document.getElementById("numerica-chart-tooltip");
        if (tooltipEl) {
            tooltipEl.style.opacity = "0";
            tooltipEl.style.display = "none";
        }
    }

    return { render, destroy, renderReportDocuments, destroyReportDocuments, renderSubscriptions, renderESign, scrollToDetails };
})();
