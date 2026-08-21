export async function init(options) {
    const pdfInput = document.getElementById(options.pdfInputId);
    const canvas = document.getElementById(options.canvasId);
    const stage = document.getElementById(options.stageId);
    const placeholder = document.getElementById(options.placeholderId);
    const previousButton = document.getElementById(options.previousButtonId);
    const nextButton = document.getElementById(options.nextButtonId);
    const currentPageLabel = document.getElementById(options.currentPageLabelId);
    const pageCountLabel = document.getElementById(options.pageCountLabelId);
    const status = document.getElementById(options.statusId);
    const thumbnails = document.getElementById(options.thumbnailsId);
    const zoomOutButton = document.getElementById(options.zoomOutButtonId);
    const zoomInButton = document.getElementById(options.zoomInButtonId);
    const zoomLabel = document.getElementById(options.zoomLabelId);

    if (!pdfInput || !canvas || !stage) {
        return null;
    }

    const pdfjs = await import("/lib/pdfjs/pdf.min.mjs");
    pdfjs.GlobalWorkerOptions.workerSrc = "/lib/pdfjs/pdf.worker.min.mjs";

    let pdfDocument = null;
    let renderTask = null;
    let pageNumber = 1;
    let zoomPercent = 100;
    let thumbnailTasks = [];

    const minZoomPercent = 50;
    const maxZoomPercent = 200;
    const zoomStepPercent = 25;

    const setStatus = (message, isError = false) => {
        if (!status) {
            return;
        }

        status.textContent = message;
        status.classList.toggle("is-error", isError);
    };

    const updateNavigation = () => {
        const total = pdfDocument?.numPages ?? 0;
        if (currentPageLabel) currentPageLabel.textContent = pageNumber.toString();
        if (pageCountLabel) pageCountLabel.textContent = total.toString();
        if (previousButton) previousButton.disabled = !pdfDocument || pageNumber <= 1;
        if (nextButton) nextButton.disabled = !pdfDocument || pageNumber >= total;
        if (zoomLabel) zoomLabel.textContent = `${zoomPercent}%`;
        if (zoomOutButton) zoomOutButton.disabled = !pdfDocument || zoomPercent <= minZoomPercent;
        if (zoomInButton) zoomInButton.disabled = !pdfDocument || zoomPercent >= maxZoomPercent;

        thumbnails?.querySelectorAll(".validation-pdf-thumb").forEach(item => {
            item.classList.toggle("is-active", Number(item.dataset.page) === pageNumber);
        });
    };

    const clearThumbnails = () => {
        thumbnailTasks.forEach(task => task?.cancel?.());
        thumbnailTasks = [];

        if (!thumbnails) {
            return;
        }

        thumbnails.innerHTML = "";
        const item = document.createElement("div");
        item.className = "validation-pdf-thumb is-placeholder";
        item.innerHTML = "<span></span><strong>1</strong>";
        thumbnails.appendChild(item);
    };

    const renderThumbnails = async () => {
        if (!pdfDocument || !thumbnails) {
            return;
        }

        clearThumbnails();
        thumbnails.innerHTML = "";

        for (let index = 1; index <= pdfDocument.numPages; index += 1) {
            const item = document.createElement("button");
            item.type = "button";
            item.className = "validation-pdf-thumb";
            item.dataset.page = index.toString();
            item.setAttribute("aria-label", `Ir a pagina ${index}`);

            const thumbCanvas = document.createElement("canvas");
            const label = document.createElement("strong");
            label.textContent = index.toString();
            item.append(thumbCanvas, label);
            item.addEventListener("click", async () => {
                pageNumber = index;
                await renderPage();
            });
            thumbnails.appendChild(item);

            try {
                const page = await pdfDocument.getPage(index);
                const viewport = page.getViewport({ scale: 1 });
                const scale = 56 / viewport.width;
                const scaledViewport = page.getViewport({ scale });
                const outputScale = Math.min(window.devicePixelRatio || 1, 2);
                const context = thumbCanvas.getContext("2d", { alpha: false });

                thumbCanvas.width = Math.floor(scaledViewport.width * outputScale);
                thumbCanvas.height = Math.floor(scaledViewport.height * outputScale);
                thumbCanvas.style.width = `${Math.floor(scaledViewport.width)}px`;
                thumbCanvas.style.height = `${Math.floor(scaledViewport.height)}px`;

                const task = page.render({
                    canvasContext: context,
                    viewport: scaledViewport,
                    transform: outputScale === 1 ? null : [outputScale, 0, 0, outputScale, 0, 0]
                });

                thumbnailTasks.push(task);
                await task.promise;
            } catch {
                item.classList.add("is-placeholder");
            }
        }

        updateNavigation();
    };

    async function renderPage() {
        if (!pdfDocument) {
            return;
        }

        pageNumber = Math.min(pdfDocument.numPages, Math.max(1, pageNumber || 1));
        updateNavigation();
        setStatus("Renderizando página...");

        if (renderTask) {
            renderTask.cancel();
        }

        const page = await pdfDocument.getPage(pageNumber);
        const baseViewport = page.getViewport({ scale: 1 });
        const availableWidth = Math.max(320, Math.min(980, stage.clientWidth - 28));
        const scale = (availableWidth / baseViewport.width) * (zoomPercent / 100);
        const viewport = page.getViewport({ scale });
        const outputScale = Math.min(window.devicePixelRatio || 1, 2);

        canvas.width = Math.floor(viewport.width * outputScale);
        canvas.height = Math.floor(viewport.height * outputScale);
        canvas.style.width = `${Math.floor(viewport.width)}px`;
        canvas.style.height = `${Math.floor(viewport.height)}px`;

        const context = canvas.getContext("2d", { alpha: false });
        renderTask = page.render({
            canvasContext: context,
            viewport,
            transform: outputScale === 1 ? null : [outputScale, 0, 0, outputScale, 0, 0]
        });

        try {
            await renderTask.promise;
            if (placeholder) placeholder.hidden = true;
            canvas.hidden = false;
            setStatus("PDF cargado correctamente.");
        } catch (error) {
            if (error?.name !== "RenderingCancelledException") {
                setStatus("No se pudo mostrar esta página.", true);
            }
        } finally {
            renderTask = null;
        }
    }

    const changeZoom = async delta => {
        if (!pdfDocument) {
            return;
        }

        const nextZoom = Math.max(minZoomPercent, Math.min(maxZoomPercent, zoomPercent + delta));
        if (nextZoom === zoomPercent) {
            updateNavigation();
            return;
        }

        zoomPercent = nextZoom;
        await renderPage();
    };

    const onPdfChange = async () => {
        const file = pdfInput.files?.[0];
        clearThumbnails();

        if (!file) {
            pdfDocument = null;
            canvas.hidden = true;
            if (placeholder) placeholder.hidden = false;
            setStatus("Carga un PDF para visualizarlo.");
            updateNavigation();
            return;
        }

        if (file.type !== "application/pdf" && !file.name.toLowerCase().endsWith(".pdf")) {
            setStatus("Selecciona un archivo PDF válido.", true);
            return;
        }

        try {
            setStatus("Abriendo PDF...");
            pdfDocument = await pdfjs.getDocument({ data: await file.arrayBuffer() }).promise;
            pageNumber = 1;
            zoomPercent = 100;
            await renderThumbnails();
            await renderPage();
        } catch {
            pdfDocument = null;
            canvas.hidden = true;
            if (placeholder) placeholder.hidden = false;
            setStatus("No fue posible abrir el PDF.", true);
            updateNavigation();
            clearThumbnails();
        }
    };

    const loadPdfData = async (data, successMessage) => {
        clearThumbnails();

        try {
            setStatus("Abriendo PDF...");
            pdfDocument = await pdfjs.getDocument({ data }).promise;
            pageNumber = 1;
            zoomPercent = 100;
            await renderThumbnails();
            await renderPage();
            setStatus(successMessage || "PDF cargado correctamente.");
        } catch {
            pdfDocument = null;
            canvas.hidden = true;
            if (placeholder) placeholder.hidden = false;
            setStatus("No fue posible abrir el PDF.", true);
            updateNavigation();
            clearThumbnails();
        }
    };

    const onPrevious = async () => {
        if (!pdfDocument || pageNumber <= 1) return;
        pageNumber -= 1;
        await renderPage();
    };

    const onNext = async () => {
        if (!pdfDocument || pageNumber >= pdfDocument.numPages) return;
        pageNumber += 1;
        await renderPage();
    };

    pdfInput.addEventListener("change", onPdfChange);
    previousButton?.addEventListener("click", onPrevious);
    nextButton?.addEventListener("click", onNext);
    zoomOutButton?.addEventListener("click", () => changeZoom(-zoomStepPercent));
    zoomInButton?.addEventListener("click", () => changeZoom(zoomStepPercent));
    updateNavigation();
    clearThumbnails();

    return {
        async loadFromUrl(url) {
            if (!url) {
                return;
            }

            const response = await fetch(url, { cache: "no-store" });
            if (!response.ok) {
                setStatus("No fue posible abrir el PDF.", true);
                return;
            }

            await loadPdfData(await response.arrayBuffer(), "PDF del historial cargado.");
        },
        dispose() {
            if (renderTask) {
                renderTask.cancel();
            }

            thumbnailTasks.forEach(task => task?.cancel?.());
            pdfInput.removeEventListener("change", onPdfChange);
            previousButton?.removeEventListener("click", onPrevious);
            nextButton?.removeEventListener("click", onNext);
        }
    };
}
