export async function init(options, dotNetRef) {
    const pdfInput = document.getElementById(options.pdfInputId);
    const canvas = document.getElementById(options.canvasId);
    const stage = document.getElementById(options.stageId);
    const placeholder = document.getElementById(options.placeholderId);
    const footprints = [
        document.getElementById(options.footprintId),
        document.getElementById(options.secondaryFootprintId)
    ];
    const resizeHandles = [
        document.getElementById(options.resizeHandleId),
        document.getElementById(options.secondaryResizeHandleId)
    ];
    const previousButton = document.getElementById(options.previousButtonId);
    const nextButton = document.getElementById(options.nextButtonId);
    const currentPageLabel = document.getElementById(options.currentPageLabelId);
    const pageCountLabel = document.getElementById(options.pageCountLabelId);
    const selectionStatus = document.getElementById(options.selectionStatusId);
    const thumbnails = document.getElementById(options.thumbnailsId);
    const zoomOutButton = document.getElementById(options.zoomOutButtonId);
    const zoomInButton = document.getElementById(options.zoomInButtonId);
    const secondPlacementToggle = document.getElementById(options.secondPlacementToggleId);

    if (!pdfInput || !canvas || !stage || !footprints[0]) {
        return null;
    }

    const pdfjs = await import("/lib/pdfjs/pdf.min.mjs");
    pdfjs.GlobalWorkerOptions.workerSrc = "/lib/pdfjs/pdf.worker.min.mjs";

    let pdfDocument = null;
    let pdfPage = null;
    let pageWidthMm = 0;
    let pageHeightMm = 0;
    let renderTask = null;
    let dragStart = null;
    let pageNumber = 1;
    let preferredSelection = null;
    let autoApplyPreferred = false;
    let thumbnailTasks = [];
    let zoomPercent = 100;
    let secondPlacementEnabled = Boolean(secondPlacementToggle?.checked);
    let activePlacementIndex = 0;
    let selectedPositions = [null, null];

    const fixedStampWidthMm = 60;
    const stampHeightRatio = 0.5;
    const minZoomPercent = 50;
    const maxZoomPercent = 200;
    const zoomStepPercent = 25;

    const normalizeSelection = selection => {
        if (!selection) {
            return null;
        }

        return {
            page: Number(selection.page ?? selection.Page ?? 1),
            xMm: Number(selection.xMm ?? selection.XMm ?? 0),
            yMm: Number(selection.yMm ?? selection.YMm ?? 0),
            widthMm: fixedStampWidthMm,
            rotation: Number(selection.rotation ?? selection.Rotacion ?? selection.rotacion ?? 0)
        };
    };

    const getRotation = () => pageWidthMm > pageHeightMm ? 90 : 0;

    const getPlacementBounds = placement => {
        const widthMm = placement.widthMm;
        const heightMm = widthMm * stampHeightRatio;
        const rotated = placement.rotation === 90 || placement.rotation === 270;
        return {
            widthMm: rotated ? heightMm : widthMm,
            heightMm: rotated ? widthMm : heightMm
        };
    };

    const setStatus = (message, isError = false) => {
        selectionStatus.textContent = message;
        selectionStatus.classList.toggle("is-error", isError);
    };

    const notifySelections = async () => {
        await dotNetRef.invokeMethodAsync(
            "OnStampSelectionsChanged",
            JSON.stringify(selectedPositions.filter(Boolean)));
    };

    const updateNavigation = () => {
        const total = pdfDocument?.numPages ?? 0;
        currentPageLabel.textContent = pageNumber.toString();
        pageCountLabel.textContent = total.toString();
        previousButton.disabled = !pdfDocument || pageNumber <= 1;
        nextButton.disabled = !pdfDocument || pageNumber >= total;

        if (zoomOutButton) {
            zoomOutButton.disabled = !pdfDocument || zoomPercent <= minZoomPercent;
        }

        if (zoomInButton) {
            zoomInButton.disabled = !pdfDocument || zoomPercent >= maxZoomPercent;
        }

        thumbnails?.querySelectorAll(".pdf-thumb").forEach(item => {
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
        item.className = "pdf-thumb is-placeholder";
        item.innerHTML = "<span></span><strong>1</strong>";
        thumbnails.appendChild(item);
    };

    const renderThumbnails = async () => {
        if (!pdfDocument || !thumbnails) {
            return;
        }

        clearThumbnails();
        thumbnails.innerHTML = "";

        for (let pageIndex = 1; pageIndex <= pdfDocument.numPages; pageIndex += 1) {
            const item = document.createElement("button");
            item.type = "button";
            item.className = "pdf-thumb";
            item.dataset.page = pageIndex.toString();
            item.setAttribute("aria-label", `Ir a página ${pageIndex}`);

            const thumbCanvas = document.createElement("canvas");
            const label = document.createElement("strong");
            label.textContent = pageIndex.toString();
            item.append(thumbCanvas, label);
            item.addEventListener("click", async () => {
                pageNumber = pageIndex;
                await renderPage();
            });
            thumbnails.appendChild(item);

            try {
                const page = await pdfDocument.getPage(pageIndex);
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

    const updateFootprint = index => {
        const footprint = footprints[index];
        const placement = selectedPositions[index];
        if (!footprint || !placement || !pdfPage || placement.page !== pageNumber) {
            if (footprint) {
                footprint.hidden = true;
            }
            return;
        }

        const displayedWidth = canvas.clientWidth;
        const displayedHeight = canvas.clientHeight;
        const bounds = getPlacementBounds(placement);
        const originalWidthPx = (placement.widthMm / pageWidthMm) * displayedWidth;
        const originalHeightPx = ((placement.widthMm * stampHeightRatio) / pageHeightMm) * displayedHeight;
        const boundsWidthPx = (bounds.widthMm / pageWidthMm) * displayedWidth;
        const boundsHeightPx = (bounds.heightMm / pageHeightMm) * displayedHeight;
        const left = canvas.offsetLeft + (placement.xMm / pageWidthMm) * displayedWidth;
        const top = canvas.offsetTop + (placement.yMm / pageHeightMm) * displayedHeight;

        footprint.style.left = `${left + ((boundsWidthPx - originalWidthPx) / 2)}px`;
        footprint.style.top = `${top + ((boundsHeightPx - originalHeightPx) / 2)}px`;
        footprint.style.width = `${originalWidthPx}px`;
        footprint.style.height = `${originalHeightPx}px`;
        footprint.style.transform = placement.rotation === 0 ? "" : `rotate(${placement.rotation}deg)`;
        footprint.classList.toggle("is-active", activePlacementIndex === index);
        footprint.classList.toggle("is-landscape", placement.rotation === 90 || placement.rotation === 270);
        footprint.hidden = false;
    };

    const updateFootprints = () => {
        updateFootprint(0);
        updateFootprint(1);
    };

    const getPagePoint = event => {
        const bounds = canvas.getBoundingClientRect();
        return {
            xMm: Math.max(0, Math.min(((event.clientX - bounds.left) / bounds.width) * pageWidthMm, pageWidthMm)),
            yMm: Math.max(0, Math.min(((event.clientY - bounds.top) / bounds.height) * pageHeightMm, pageHeightMm))
        };
    };

    const applySelection = async (rawX, rawY, index, shouldNotify = true) => {
        const widthMm = Math.min(fixedStampWidthMm, pageWidthMm, pageHeightMm / stampHeightRatio);
        const rotation = getRotation();
        const bounds = getPlacementBounds({ widthMm, rotation });
        const xMm = Math.max(0, Math.min(rawX, pageWidthMm - bounds.widthMm));
        const yMm = Math.max(0, Math.min(rawY, pageHeightMm - bounds.heightMm));

        selectedPositions[index] = {
            page: pageNumber,
            xMm,
            yMm,
            widthMm,
            rotation
        };
        activePlacementIndex = index;
        updateFootprints();
        setStatus(
            `Ubicación Firma ${index + 1}: X ${xMm.toFixed(2)} mm - Y ${yMm.toFixed(2)} mm${rotation ? " - cuadro girado 90°" : ""}`);

        if (shouldNotify) {
            await notifySelections();
        }
    };

    const renderPage = async () => {
        if (!pdfDocument) {
            return;
        }

        pageNumber = Math.min(pdfDocument.numPages, Math.max(1, pageNumber || 1));
        updateNavigation();
        setStatus("Renderizando página...");

        if (renderTask) {
            renderTask.cancel();
        }

        pdfPage = await pdfDocument.getPage(pageNumber);
        const baseViewport = pdfPage.getViewport({ scale: 1 });
        const availableWidth = Math.max(280, Math.min(820, stage.clientWidth - 24));
        const scale = (availableWidth / baseViewport.width) * (zoomPercent / 100);
        const viewport = pdfPage.getViewport({ scale });
        const outputScale = Math.min(window.devicePixelRatio || 1, 2);

        canvas.width = Math.floor(viewport.width * outputScale);
        canvas.height = Math.floor(viewport.height * outputScale);
        canvas.style.width = `${Math.floor(viewport.width)}px`;
        canvas.style.height = `${Math.floor(viewport.height)}px`;

        const shell = stage.closest(".pdf-view-shell");
        shell?.style.setProperty(
            "--stamp-pdf-page-height",
            `${Math.max(390, Math.floor(viewport.height) + 32)}px`);

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
            updateFootprints();
            setStatus(pageWidthMm > pageHeightMm
                ? "Página horizontal: el cuadro se gira automáticamente 90°."
                : "Haz clic o arrastra cada recuadro para ubicar las firmas.");
            await dotNetRef.invokeMethodAsync("OnStampPageChanged", pageNumber);
            if (!selectedPositions[0] && autoApplyPreferred && preferredSelection?.page === pageNumber) {
                await applySelection(preferredSelection.xMm, preferredSelection.yMm, 0);
            }
        } catch (error) {
            if (error?.name !== "RenderingCancelledException") {
                setStatus("No se pudo mostrar esta página.", true);
            }
        } finally {
            renderTask = null;
        }
    };

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
        updateNavigation();
        await renderPage();
    };

    const loadPdfBytes = async (arrayBuffer, preferredPage) => {
        selectedPositions = [null, null];
        activePlacementIndex = 0;
        footprints.forEach(footprint => {
            if (footprint) {
                footprint.hidden = true;
            }
        });
        clearThumbnails();

        if (!arrayBuffer) {
            return;
        }

        try {
            setStatus("Abriendo PDF...");
            pdfDocument = await pdfjs.getDocument({ data: arrayBuffer }).promise;
            zoomPercent = 100;
            pageNumber = preferredPage
                ? Math.min(pdfDocument.numPages, Math.max(1, preferredPage))
                : autoApplyPreferred && preferredSelection?.page
                    ? Math.min(pdfDocument.numPages, Math.max(1, preferredSelection.page))
                    : 1;
            await renderThumbnails();
            await renderPage();
        } catch {
            pdfDocument = null;
            canvas.hidden = true;
            placeholder.hidden = false;
            setStatus("No fue posible abrir el PDF.", true);
            updateNavigation();
            clearThumbnails();
        }
    };

    const onPdfChange = async () => {
        const file = pdfInput.files?.[0];
        if (!file) {
            return;
        }

        if (file.type !== "application/pdf" && !file.name.toLowerCase().endsWith(".pdf")) {
            setStatus("Selecciona un archivo PDF válido.", true);
            return;
        }

        await loadPdfBytes(await file.arrayBuffer());
    };

    const onCanvasPointerDown = async event => {
        if (!pdfPage || event.button !== 0) {
            return;
        }

        event.preventDefault();
        const index = secondPlacementEnabled && selectedPositions[0] && !selectedPositions[1]
            ? 1
            : activePlacementIndex;
        const point = getPagePoint(event);
        await applySelection(point.xMm, point.yMm, index);
    };

    const onFootprintPointerDown = (index, event) => {
        const placement = selectedPositions[index];
        if (!placement || event.button !== 0) {
            return;
        }

        event.preventDefault();
        event.stopPropagation();
        activePlacementIndex = index;
        updateFootprints();
        const point = getPagePoint(event);
        dragStart = {
            pointerId: event.pointerId,
            index,
            offsetXMm: point.xMm - placement.xMm,
            offsetYMm: point.yMm - placement.yMm
        };

        footprints[index].setPointerCapture(event.pointerId);
        stage.classList.add("is-dragging");
        setStatus(`Arrastra Ubicación Firma ${index + 1} para ajustar la ubicación.`);
    };

    const onFootprintPointerMove = async event => {
        if (!dragStart || dragStart.pointerId !== event.pointerId) {
            return;
        }

        if ((event.buttons & 1) === 0) {
            finishDragging(event);
            return;
        }

        event.preventDefault();
        const point = getPagePoint(event);
        await applySelection(
            point.xMm - dragStart.offsetXMm,
            point.yMm - dragStart.offsetYMm,
            dragStart.index);
    };

    const finishDragging = event => {
        if (!dragStart || dragStart.pointerId !== event.pointerId) {
            return;
        }

        const footprint = footprints[dragStart.index];
        if (footprint?.hasPointerCapture(event.pointerId)) {
            footprint.releasePointerCapture(event.pointerId);
        }

        dragStart = null;
        stage.classList.remove("is-dragging");
    };

    const onResizePointerDown = event => {
        event.preventDefault();
        event.stopPropagation();
    };

    const setSecondPlacementEnabled = async enabled => {
        secondPlacementEnabled = Boolean(enabled);
        if (secondPlacementToggle) {
            secondPlacementToggle.checked = secondPlacementEnabled;
        }

        if (!secondPlacementEnabled) {
            selectedPositions[1] = null;
            activePlacementIndex = 0;
            updateFootprints();
            await notifySelections();
        } else {
            activePlacementIndex = selectedPositions[0] ? 1 : 0;
            setStatus(selectedPositions[0]
                ? "Haz clic en el PDF para ubicar la segunda firma."
                : "Haz clic en el PDF para ubicar la primera firma.");
        }
    };

    const onPrevious = async () => {
        pageNumber = Math.max(1, pageNumber - 1);
        await renderPage();
    };

    const onNext = async () => {
        pageNumber = Math.min(pdfDocument?.numPages ?? 1, pageNumber + 1);
        await renderPage();
    };

    const onZoomOut = async () => await changeZoom(-zoomStepPercent);
    const onZoomIn = async () => await changeZoom(zoomStepPercent);

    pdfInput.addEventListener("change", onPdfChange);
    canvas.addEventListener("pointerdown", onCanvasPointerDown);
    footprints.forEach((footprint, index) => {
        if (!footprint) {
            return;
        }

        footprint.addEventListener("pointerdown", event => onFootprintPointerDown(index, event));
        footprint.addEventListener("pointermove", onFootprintPointerMove);
        footprint.addEventListener("pointerup", finishDragging);
        footprint.addEventListener("pointercancel", finishDragging);
    });
    resizeHandles.forEach(handle => handle?.addEventListener("pointerdown", onResizePointerDown));
    window.addEventListener("pointerup", finishDragging);
    window.addEventListener("pointercancel", finishDragging);
    previousButton.addEventListener("click", onPrevious);
    nextButton.addEventListener("click", onNext);
    zoomOutButton?.addEventListener("click", onZoomOut);
    zoomInButton?.addEventListener("click", onZoomIn);
    updateNavigation();

    return {
        async setPreferredSelection(selection, autoApply) {
            preferredSelection = normalizeSelection(selection);
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
                await applySelection(preferredSelection.xMm, preferredSelection.yMm, 0);
            }
        },
        async setSecondPlacementEnabled(enabled) {
            await setSecondPlacementEnabled(enabled);
        },
        getSelections() {
            return {
                pageCount: pdfDocument?.numPages ?? 0,
                positions: selectedPositions.filter(Boolean)
            };
        },
        async loadFromUrl(url) {
            if (!url) {
                pdfDocument = null;
                pdfPage = null;
                selectedPositions = [null, null];
                footprints.forEach(footprint => {
                    if (footprint) {
                        footprint.hidden = true;
                    }
                });
                canvas.hidden = true;
                placeholder.hidden = false;
                pageNumber = 1;
                setStatus("Carga un PDF para seleccionar la posición.");
                updateNavigation();
                clearThumbnails();
                return;
            }

            try {
                setStatus("Abriendo PDF guardado...");
                const response = await fetch(url, { cache: "no-store" });
                if (!response.ok) {
                    throw new Error("No se pudo leer el documento.");
                }

                await loadPdfBytes(await response.arrayBuffer());
            } catch {
                pdfDocument = null;
                canvas.hidden = true;
                placeholder.hidden = false;
                setStatus("No fue posible abrir el PDF guardado.", true);
                updateNavigation();
                clearThumbnails();
            }
        },
        async getPreviewSnapshot() {
            const placement = selectedPositions[0];
            if (!pdfDocument || !placement || canvas.hidden) {
                return null;
            }

            const displayedWidth = canvas.clientWidth;
            const displayedHeight = canvas.clientHeight;
            const bounds = getPlacementBounds(placement);
            return {
                imageDataUrl: canvas.toDataURL("image/png"),
                width: displayedWidth,
                height: displayedHeight,
                left: (placement.xMm / pageWidthMm) * displayedWidth,
                top: (placement.yMm / pageHeightMm) * displayedHeight,
                stampWidth: (bounds.widthMm / pageWidthMm) * displayedWidth,
                stampHeight: (bounds.heightMm / pageHeightMm) * displayedHeight,
                page: placement.page,
                placements: selectedPositions.filter(Boolean)
            };
        },
        dispose() {
            clearThumbnails();
            pdfInput.removeEventListener("change", onPdfChange);
            canvas.removeEventListener("pointerdown", onCanvasPointerDown);
            footprints.forEach(footprint => {
                footprint?.removeEventListener("pointermove", onFootprintPointerMove);
                footprint?.removeEventListener("pointerup", finishDragging);
                footprint?.removeEventListener("pointercancel", finishDragging);
            });
            resizeHandles.forEach(handle => handle?.removeEventListener("pointerdown", onResizePointerDown));
            window.removeEventListener("pointerup", finishDragging);
            window.removeEventListener("pointercancel", finishDragging);
            previousButton.removeEventListener("click", onPrevious);
            nextButton.removeEventListener("click", onNext);
            zoomOutButton?.removeEventListener("click", onZoomOut);
            zoomInButton?.removeEventListener("click", onZoomIn);
        }
    };
}
