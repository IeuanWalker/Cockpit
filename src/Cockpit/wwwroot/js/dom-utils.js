(() => {
    const cockpit = window.cockpit ??= {};
    const pendingAutoResizeFrames = new WeakMap();
    const maxTextareaHeightPx = 300;
    const autoHeight = 'auto';

    function getElementById(elementId) {
        return typeof elementId === 'string' && elementId.length > 0
            ? document.getElementById(elementId)
            : null;
    }

    function getTextareaById(elementId) {
        const element = getElementById(elementId);
        return element instanceof HTMLTextAreaElement ? element : null;
    }

    function cancelPendingAutoResize(textarea) {
        const frameId = pendingAutoResizeFrames.get(textarea);
        if (frameId === undefined) {
            return;
        }

        window.cancelAnimationFrame(frameId);
        pendingAutoResizeFrames.delete(textarea);
    }

    function applyAutoResize(textarea) {
        textarea.style.height = autoHeight;
        textarea.style.height = `${Math.min(textarea.scrollHeight, maxTextareaHeightPx)}px`;
    }

    cockpit.autoResizeTextarea = (elementId) => {
        const textarea = getTextareaById(elementId);
        if (!textarea) {
            return;
        }

        cancelPendingAutoResize(textarea);

        const frameId = window.requestAnimationFrame(() => {
            pendingAutoResizeFrames.delete(textarea);
            applyAutoResize(textarea);
        });

        pendingAutoResizeFrames.set(textarea, frameId);
    };
})();
