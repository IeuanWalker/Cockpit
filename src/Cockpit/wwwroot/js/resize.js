/*
 * Shared horizontal resize support for sidebar and split-panel handles.
 * JavaScript owns the mouse event lifecycle and DOM updates.
 * .NET owns persisted UI state through a single OnResize notification when dragging ends.
 */
(function () {
    window.cockpit ??= {};
    const cockpit = window.cockpit;

    const resizeCursor = 'col-resize';
    const sidebarWidthRange = Object.freeze({ min: 150, max: 600 });
    const splitPanelWidthRange = Object.freeze({ min: 120, max: 500 });
    const resizeCleanupByHandle = new WeakMap();
    const resizeCleanupByHandleId = new Map();

    function clampToRange(value, range) {
        return Math.min(range.max, Math.max(range.min, value));
    }

    function setDocumentResizeState(isDragging) {
        const body = document.body;
        if (!body) {
            return;
        }

        body.style.cursor = isDragging ? resizeCursor : '';
        body.style.userSelect = isDragging ? 'none' : '';
    }

    function getElementById(id) {
        return typeof id === 'string' ? document.getElementById(id) : null;
    }

    function setElementWidth(element, width) {
        element.style.width = `${width}px`;
    }

    function disposeHorizontalResize(handle) {
        if (!handle) {
            return;
        }

        const cleanup = resizeCleanupByHandle.get(handle);
        cleanup?.();
    }

    function disposeHorizontalResizeById(handleId) {
        if (typeof handleId !== 'string' || handleId.length === 0) {
            return;
        }

        const cleanup = resizeCleanupByHandleId.get(handleId);
        cleanup?.();
    }

    function reportWidthChanged(onWidthChanged, width) {
        if (typeof onWidthChanged !== 'function') {
            return;
        }

        try {
            const result = onWidthChanged(width);
            if (result && typeof result.catch === 'function') {
                result.catch((error) => {
                    console.error('Failed to report resize change.', error);
                });
            }
        } catch (error) {
            console.error('Failed to report resize change.', error);
        }
    }

    /**
     * Wires a drag handle to horizontal width calculations.
     *
     * Reinitializing the same handle automatically disposes the previous listeners.
     * `getNextWidth` should return a finite number while dragging can continue.
     * Return null/undefined when layout state is invalid and the drag should stop.
     *
     * @param {{
     *   handle: HTMLElement | null,
     *   handleId?: string,
     *   getNextWidth: (event: MouseEvent) => number | null | undefined,
     *   applyWidth: (width: number) => void,
     *   onWidthChanged?: (width: number) => void | Promise<void>
     * }} options
     * @returns {(() => void) | undefined}
     */
    function attachHorizontalResize({ handle, handleId, getNextWidth, applyWidth, onWidthChanged }) {
        if (!handle || typeof getNextWidth !== 'function' || typeof applyWidth !== 'function') {
            return undefined;
        }

        disposeHorizontalResize(handle);
        disposeHorizontalResizeById(handleId);

        let isDragging = false;
        let lastAppliedWidth;
        let pendingWidth;
        let animationFrame;
        let dragOverlay;

        const applyPendingWidth = () => {
            if (animationFrame !== undefined) {
                window.cancelAnimationFrame?.(animationFrame);
                animationFrame = undefined;
            }
            if (!Number.isFinite(pendingWidth) || pendingWidth === lastAppliedWidth) {
                pendingWidth = undefined;
                return;
            }

            lastAppliedWidth = pendingWidth;
            pendingWidth = undefined;
            applyWidth(lastAppliedWidth);
        };

        const scheduleWidth = (width) => {
            pendingWidth = width;
            if (animationFrame !== undefined) {
                return;
            }
            if (typeof window.requestAnimationFrame !== 'function') {
                applyPendingWidth();
                return;
            }
            animationFrame = window.requestAnimationFrame(() => {
                animationFrame = undefined;
                applyPendingWidth();
            });
        };

        const stopDrag = () => {
            if (!isDragging) {
                return;
            }

            applyPendingWidth();
            isDragging = false;
            handle.classList.remove('resizing');
            setDocumentResizeState(false);
            dragOverlay?.remove();
            dragOverlay = undefined;
            document.removeEventListener('mousemove', onMouseMove);
            document.removeEventListener('mouseup', stopDrag);
            window.removeEventListener('blur', stopDrag);
            if (Number.isFinite(lastAppliedWidth)) {
                reportWidthChanged(onWidthChanged, lastAppliedWidth);
            }
        };

        const onMouseMove = (event) => {
            if (!isDragging) {
                return;
            }

            const nextWidth = getNextWidth(event);
            if (!Number.isFinite(nextWidth)) {
                stopDrag();
                return;
            }

            const roundedWidth = Math.round(nextWidth);
            if (roundedWidth === lastAppliedWidth || roundedWidth === pendingWidth) {
                return;
            }

            scheduleWidth(roundedWidth);
        };

        const onMouseDown = (event) => {
            if (isDragging || event.button !== 0 || !handle.isConnected) {
                return;
            }

            event.preventDefault();

            isDragging = true;
            lastAppliedWidth = undefined;
            pendingWidth = undefined;
            handle.classList.add('resizing');
            setDocumentResizeState(true);
            dragOverlay = document.createElement('div');
            dragOverlay.className = 'resize-drag-overlay';
            dragOverlay.setAttribute('aria-hidden', 'true');
            Object.assign(dragOverlay.style, {
                position: 'fixed',
                inset: '0',
                zIndex: '2147483647',
                cursor: resizeCursor,
                background: 'transparent'
            });
            document.body.appendChild(dragOverlay);
            document.addEventListener('mousemove', onMouseMove);
            document.addEventListener('mouseup', stopDrag);
            window.addEventListener('blur', stopDrag);
        };

        const cleanup = () => {
            stopDrag();
            handle.removeEventListener('mousedown', onMouseDown);

            if (resizeCleanupByHandle.get(handle) === cleanup) {
                resizeCleanupByHandle.delete(handle);
            }

            if (resizeCleanupByHandleId.get(handleId) === cleanup) {
                resizeCleanupByHandleId.delete(handleId);
            }
        };

        handle.addEventListener('mousedown', onMouseDown);
        resizeCleanupByHandle.set(handle, cleanup);

        if (typeof handleId === 'string' && handleId.length > 0) {
            resizeCleanupByHandleId.set(handleId, cleanup);
        }

        return cleanup;
    }

    function getSidebarWidth(side, clientX) {
        if (side === 'left') {
            return clampToRange(clientX, sidebarWidthRange);
        }

        if (side === 'right') {
            return clampToRange(window.innerWidth - clientX, sidebarWidthRange);
        }

        return null;
    }

    function getSplitPanelWidth(leftPanel, clientX) {
        const container = leftPanel?.parentElement;
        if (!container) {
            return null;
        }

        const containerRect = container.getBoundingClientRect();
        return clampToRange(clientX - containerRect.left, splitPanelWidthRange);
    }

    cockpit.initializeResize = function (handleId, sidebarId, side, dotnetHelper) {
        const handle = getElementById(handleId);
        const sidebar = getElementById(sidebarId);

        if (!handle || !sidebar) {
            return;
        }

        attachHorizontalResize({
            handle,
            handleId,
            getNextWidth: (event) => handle.isConnected && sidebar.isConnected
                ? getSidebarWidth(side, event.clientX)
                : null,
            applyWidth: (width) => {
                setElementWidth(sidebar, width);
            },
            onWidthChanged: (width) => dotnetHelper?.invokeMethodAsync('OnResize', width)
        });
    };

    cockpit.initializePanelSplit = function (leftPanelId, handleId) {
        const leftPanel = getElementById(leftPanelId);
        const handle = getElementById(handleId);

        if (!leftPanel || !handle) {
            return;
        }

        attachHorizontalResize({
            handle,
            handleId,
            getNextWidth: (event) => handle.isConnected && leftPanel.isConnected
                ? getSplitPanelWidth(leftPanel, event.clientX)
                : null,
            applyWidth: (width) => {
                setElementWidth(leftPanel, width);
            }
        });
    };

    cockpit.disposeHorizontalResize = function (handleId) {
        disposeHorizontalResizeById(handleId);
    };
})();
