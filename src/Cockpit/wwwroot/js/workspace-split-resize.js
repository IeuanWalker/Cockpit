(function () {
    window.cockpit ??= {};

    const minimumRatio = 0.1;
    const maximumRatio = 0.9;

    function clampRatio(value) {
        return Math.min(maximumRatio, Math.max(minimumRatio, value));
    }

    function splitTemplate(axis, ratio) {
        const tracks = `minmax(0,${ratio}fr) 4px minmax(0,${1 - ratio}fr)`;
        return axis === 'rows'
            ? { property: 'gridTemplateRows', tracks }
            : { property: 'gridTemplateColumns', tracks };
    }

    window.cockpit.initializeWorkspaceSplitResize = function (sidebarId, helper) {
        const root = document.getElementById(sidebarId);
        if (!root) return;

        root.workspaceSplitResizeDispose?.();
        const listeners = new AbortController();
        const options = { capture: true, signal: listeners.signal };
        let resize = null;

        function setDocumentState(active, axis) {
            document.body?.classList.toggle('workspace-split-resizing', active);
            if (document.body) {
                document.body.style.cursor = active ? (axis === 'rows' ? 'n-resize' : 'e-resize') : '';
                document.body.style.userSelect = active ? 'none' : '';
            }
        }

        function ratioAt(event) {
            if (!resize) return null;
            const bounds = resize.container.getBoundingClientRect();
            const size = resize.axis === 'rows' ? bounds.height : bounds.width;
            if (size <= 0) return null;
            const offset = resize.axis === 'rows' ? event.clientY - bounds.top : event.clientX - bounds.left;
            return clampRatio(offset / size);
        }

        function applyRatio(ratio) {
            const template = splitTemplate(resize.axis, ratio);
            resize.container.style[template.property] = template.tracks;
            resize.ratio = ratio;
        }

        function clear(restore) {
            if (!resize) return;
            const previous = resize;
            resize = null;
            if (restore) previous.container.setAttribute('style', previous.originalStyle);
            previous.divider.classList.remove('resizing');
            setDocumentState(false, previous.axis);
            if (root.hasPointerCapture?.(previous.pointerId)) root.releasePointerCapture(previous.pointerId);
        }

        root.addEventListener('pointerdown', event => {
            const divider = event.target.closest?.('.workspace-divider[data-workspace-split-id]');
            if (!divider || !root.contains(divider) || event.button !== 0 || !event.isPrimary) return;
            const container = divider.parentElement;
            const axis = divider.dataset.workspaceAxis;
            if (!container?.classList.contains('workspace-split') || (axis !== 'columns' && axis !== 'rows')) return;

            clear(true);
            resize = {
                divider,
                container,
                axis,
                pointerId: event.pointerId,
                splitId: divider.dataset.workspaceSplitId,
                ratio: clampRatio(Number(divider.dataset.workspaceSplitRatio) || 0.5),
                originalStyle: container.getAttribute('style') || ''
            };
            event.preventDefault();
            root.setPointerCapture?.(event.pointerId);
            divider.classList.add('resizing');
            setDocumentState(true, axis);
        }, options);

        root.addEventListener('pointermove', event => {
            if (!resize || event.pointerId !== resize.pointerId) return;
            const ratio = ratioAt(event);
            if (ratio === null) return;
            event.preventDefault();
            applyRatio(ratio);
        }, options);

        window.addEventListener('pointerup', event => {
            if (!resize || event.pointerId !== resize.pointerId) return;
            const completed = resize;
            const ratio = ratioAt(event) ?? completed.ratio;
            applyRatio(ratio);
            clear(false);
            helper?.invokeMethodAsync('OnWorkspaceSplitResize', completed.splitId, ratio)
                .catch(error => console.error('Workspace split resize failed', error));
        }, options);

        root.addEventListener('pointercancel', () => clear(true), options);
        root.addEventListener('lostpointercapture', () => clear(true), options);
        window.addEventListener('blur', () => clear(true), options);
        document.addEventListener('keydown', event => {
            if (event.key === 'Escape') clear(true);
        }, options);

        root.workspaceSplitResizeDispose = () => {
            clear(true);
            listeners.abort();
        };
    };
})();
