export async function init(options, dotNetRef) {
    const pdfInput = document.getElementById(options.pdfInputId);
    const canvas = document.getElementById(options.canvasId);
    const stage = document.getElementById(options.stageId);
    const placeholder = document.getElementById(options.placeholderId);
    const footprint = document.getElementById(options.footprintId);
    const resizeHandle = document.getElementById(options.resizeHandleId);
    const previousButton = document.getElementById(options.previousButtonId);
    const nextButton = document.getElementById(options.nextButtonId);
    const currentPageLabel = document.getElementById(options.currentPageLabelId);
    const pageCountLabel = document.getElementById(options.pageCountLabelId);
    const selectionStatus = document.getElementById(options.selectionStatusId);

    if (!pdfInput || !canvas || !stage || !resizeHandle) {
        return null;
    }

    const pdfjs = await import("/lib/pdfjs/pdf.min.mjs");
    pdfjs.GlobalWorkerOptions.workerSrc = "/lib/pdfjs/pdf.worker.min.mjs";

    let pdfDocument = null;
    let pdfPage = null;
    let pageWidthMm = 0;
    let pageHeightMm = 0;
    let renderTask = null;
    let selectedPosition = null;
    let drawingStart = null;
    let resizeStart = null;
    let pointerMoved = false;
    let pageNumber = 1;
    let preferredSelection = null;
    let autoApplyPreferred = false;

    const minimumStampWidthMm = 55;
    const maximumStampWidthMm = 120;

    const normalizeSelection = selection => {
        if (!selection) {
            return null;
        }

        return {
            page: Number(selection.page ?? selection.Page ?? 1),
            xMm: Number(selection.xMm ?? selection.XMm ?? 0),
            yMm: Number(selection.yMm ?? selection.YMm ?? 0),
            widthMm: Number(selection.widthMm ?? selection.WidthMm ?? 80)
        };
    };

    const setStatus = (message, isError = false) => {
        selectionStatus.textContent = message;
        selectionStatus.classList.toggle("is-error", isError);
    };

    const notifySelection = async () => {
        if (!selectedPosition) {
            return;
        }

        await dotNetRef.invokeMethodAsync(
            "OnStampSelectionChanged",
            selectedPosition.page,
            selectedPosition.xMm,
            selectedPosition.yMm,
            selectedPosition.widthMm);
    };

    const updateNavigation = () => {
        const total = pdfDocument?.numPages ?? 0;
        currentPageLabel.textContent = pageNumber.toString();
        pageCountLabel.textContent = total.toString();
        previousButton.disabled = !pdfDocument || pageNumber <= 1;
        nextButton.disabled = !pdfDocument || pageNumber >= total;
    };

    const updateFootprint = () => {
        if (!selectedPosition || !pdfPage) {
            footprint.hidden = true;
            return;
        }

        const displayedWidth = canvas.clientWidth;
        const displayedHeight = canvas.clientHeight;
        footprint.style.left = `${canvas.offsetLeft + (selectedPosition.xMm / pageWidthMm) * displayedWidth}px`;
        footprint.style.top = `${canvas.offsetTop + (selectedPosition.yMm / pageHeightMm) * displayedHeight}px`;
        footprint.style.width = `${(selectedPosition.widthMm / pageWidthMm) * displayedWidth}px`;
        footprint.style.height = `${((selectedPosition.widthMm * 0.4) / pageHeightMm) * displayedHeight}px`;
        footprint.hidden = false;
    };

    const getPagePoint = event => {
        const bounds = canvas.getBoundingClientRect();
        return {
            xMm: Math.max(0, Math.min(((event.clientX - bounds.left) / bounds.width) * pageWidthMm, pageWidthMm)),
            yMm: Math.max(0, Math.min(((event.clientY - bounds.top) / bounds.height) * pageHeightMm, pageHeightMm))
        };
    };

    const applySelection = async (rawX, rawY, requestedWidth, shouldNotify = true) => {
        const widthMm = Math.min(
            maximumStampWidthMm,
            pageWidthMm,
            pageHeightMm / 0.4,
            Math.max(minimumStampWidthMm, requestedWidth));
        const xMm = Math.max(0, Math.min(rawX, pageWidthMm - widthMm));
        const yMm = Math.max(0, Math.min(rawY, pageHeightMm - (widthMm * 0.4)));

        selectedPosition = {
            page: pageNumber,
            xMm,
            yMm,
            widthMm
        };

        updateFootprint();
        setStatus(`Posicion: X ${xMm.toFixed(2)} mm - Y ${yMm.toFixed(2)} mm - Ancho ${widthMm.toFixed(2)} mm`);

        if (shouldNotify) {
            await notifySelection();
        }
    };

    const renderPage = async () => {
        if (!pdfDocument) {
            return;
        }

        pageNumber = Math.min(pdfDocument.numPages, Math.max(1, pageNumber || 1));
        updateNavigation();
        setStatus("Renderizando pagina...");

        if (renderTask) {
            renderTask.cancel();
        }

        pdfPage = await pdfDocument.getPage(pageNumber);
        const baseViewport = pdfPage.getViewport({ scale: 1 });
        const availableWidth = Math.max(280, Math.min(820, stage.clientWidth - 24));
        const scale = availableWidth / baseViewport.width;
        const viewport = pdfPage.getViewport({ scale });
        const outputScale = Math.min(window.devicePixelRatio || 1, 2);

        canvas.width = Math.floor(viewport.width * outputScale);
        canvas.height = Math.floor(viewport.height * outputScale);
        canvas.style.width = `${Math.floor(viewport.width)}px`;
        canvas.style.height = `${Math.floor(viewport.height)}px`;
        pageWidthMm = (baseViewport.width * 25.4) / 72;
        pageHeightMm = (baseViewport.height * 25.4) / 72;

        const context = canvas.getContext("2d", { alpha: false });
        renderTask = pdfPage.render({
            canvasContext: context,
            viewport,
            transform: outputScale === 1 ? null : [outputScale, 0, 0, outputScale, 0, 0]
        });

        try {
            await renderTask.promise;
            placeholder.hidden = true;
            canvas.hidden = false;
            selectedPosition = pageNumber === selectedPosition?.page ? selectedPosition : null;
            updateFootprint();
            setStatus("Haz clic para ubicar o arrastra hacia abajo y a la derecha para dibujar el sello.");
            await dotNetRef.invokeMethodAsync("OnStampPageChanged", pageNumber);
            if (!selectedPosition && autoApplyPreferred && preferredSelection?.page === pageNumber) {
                await applySelection(
                    preferredSelection.xMm,
                    preferredSelection.yMm,
                    preferredSelection.widthMm);
            }
        } catch (error) {
            if (error?.name !== "RenderingCancelledException") {
                setStatus("No se pudo mostrar esta pagina.", true);
            }
        } finally {
            renderTask = null;
        }
    };

    const onPdfChange = async () => {
        const file = pdfInput.files?.[0];
        selectedPosition = null;
        footprint.hidden = true;

        if (!file) {
            return;
        }

        if (file.type !== "application/pdf" && !file.name.toLowerCase().endsWith(".pdf")) {
            setStatus("Selecciona un archivo PDF valido.", true);
            return;
        }

        try {
            setStatus("Abriendo PDF...");
            pdfDocument = await pdfjs.getDocument({ data: await file.arrayBuffer() }).promise;
            pageNumber = autoApplyPreferred && preferredSelection?.page
                ? Math.min(pdfDocument.numPages, Math.max(1, preferredSelection.page))
                : 1;
            await renderPage();
        } catch {
            pdfDocument = null;
            canvas.hidden = true;
            placeholder.hidden = false;
            setStatus("No fue posible abrir el PDF.", true);
            updateNavigation();
        }
    };

    const onCanvasPointerDown = async event => {
        if (!pdfPage || event.button !== 0) {
            return;
        }

        event.preventDefault();
        const point = getPagePoint(event);
        const currentWidth = selectedPosition?.widthMm || 80;
        await applySelection(point.xMm, point.yMm, currentWidth);
        drawingStart = {
            pointerId: event.pointerId,
            xMm: selectedPosition.xMm,
            yMm: selectedPosition.yMm,
            clientX: event.clientX,
            clientY: event.clientY
        };
        pointerMoved = false;
        canvas.setPointerCapture(event.pointerId);
        stage.classList.add("is-drawing");
    };

    const onCanvasPointerMove = async event => {
        if (!drawingStart || drawingStart.pointerId !== event.pointerId) {
            return;
        }

        event.preventDefault();
        const point = getPagePoint(event);
        const deltaPixels = Math.hypot(event.clientX - drawingStart.clientX, event.clientY - drawingStart.clientY);
        pointerMoved ||= deltaPixels > 4;

        if (!pointerMoved) {
            return;
        }

        const requestedWidth = Math.max(
            minimumStampWidthMm,
            point.xMm - drawingStart.xMm,
            (point.yMm - drawingStart.yMm) / 0.4);
        await applySelection(drawingStart.xMm, drawingStart.yMm, requestedWidth);
    };

    const finishDrawing = event => {
        if (!drawingStart || drawingStart.pointerId !== event.pointerId) {
            return;
        }

        if (canvas.hasPointerCapture(event.pointerId)) {
            canvas.releasePointerCapture(event.pointerId);
        }

        drawingStart = null;
        stage.classList.remove("is-drawing");
    };

    const onResizePointerDown = event => {
        if (!selectedPosition || event.button !== 0) {
            return;
        }

        event.preventDefault();
        event.stopPropagation();
        resizeStart = {
            pointerId: event.pointerId,
            xMm: selectedPosition.xMm,
            yMm: selectedPosition.yMm
        };
        stage.classList.add("is-resizing");
        setStatus("Arrastra la esquina para cambiar el tamano.");
    };

    const onWindowPointerMove = async event => {
        if (!resizeStart || resizeStart.pointerId !== event.pointerId) {
            return;
        }

        event.preventDefault();
        const point = getPagePoint(event);
        const requestedWidth = Math.max(
            minimumStampWidthMm,
            point.xMm - resizeStart.xMm,
            (point.yMm - resizeStart.yMm) / 0.4);
        await applySelection(resizeStart.xMm, resizeStart.yMm, requestedWidth);
    };

    const finishResize = event => {
        if (!resizeStart || resizeStart.pointerId !== event.pointerId) {
            return;
        }

        resizeStart = null;
        stage.classList.remove("is-resizing");
    };

    const onPrevious = async () => {
        pageNumber = Math.max(1, pageNumber - 1);
        await renderPage();
    };

    const onNext = async () => {
        pageNumber = Math.min(pdfDocument?.numPages ?? 1, pageNumber + 1);
        await renderPage();
    };

    pdfInput.addEventListener("change", onPdfChange);
    canvas.addEventListener("pointerdown", onCanvasPointerDown);
    canvas.addEventListener("pointermove", onCanvasPointerMove);
    canvas.addEventListener("pointerup", finishDrawing);
    canvas.addEventListener("pointercancel", finishDrawing);
    resizeHandle.addEventListener("pointerdown", onResizePointerDown);
    window.addEventListener("pointermove", onWindowPointerMove);
    window.addEventListener("pointerup", finishResize);
    window.addEventListener("pointercancel", finishResize);
    previousButton.addEventListener("click", onPrevious);
    nextButton.addEventListener("click", onNext);
    updateNavigation();

    return {
        async setPreferredSelection(selection, autoApply) {
            preferredSelection = normalizeSelection(selection);
            autoApplyPreferred = Boolean(autoApply);
        },
        async setAutoApplyPreferred(autoApply) {
            autoApplyPreferred = Boolean(autoApply);
        },
        async applyPreferredSelection(selection) {
            preferredSelection = normalizeSelection(selection) || preferredSelection;
            if (!preferredSelection) {
                return;
            }

            if (pdfDocument && preferredSelection.page !== pageNumber) {
                pageNumber = Math.min(pdfDocument.numPages, Math.max(1, preferredSelection.page));
                await renderPage();
            }

            if (pdfDocument) {
                await applySelection(
                    preferredSelection.xMm,
                    preferredSelection.yMm,
                    preferredSelection.widthMm);
            }
        },
        dispose() {
            pdfInput.removeEventListener("change", onPdfChange);
            canvas.removeEventListener("pointerdown", onCanvasPointerDown);
            canvas.removeEventListener("pointermove", onCanvasPointerMove);
            canvas.removeEventListener("pointerup", finishDrawing);
            canvas.removeEventListener("pointercancel", finishDrawing);
            resizeHandle.removeEventListener("pointerdown", onResizePointerDown);
            window.removeEventListener("pointermove", onWindowPointerMove);
            window.removeEventListener("pointerup", finishResize);
            window.removeEventListener("pointercancel", finishResize);
            previousButton.removeEventListener("click", onPrevious);
            nextButton.removeEventListener("click", onNext);
        }
    };
}
