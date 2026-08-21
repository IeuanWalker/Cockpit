(() => {
    const cockpit = window.cockpit ??= {};
    const pendingAutoResizeFrames = new WeakMap();
    const sessionTooltipStateByElement = new WeakMap();
    let sessionListLoadMoreObserver = null;
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

    function closeSessionTooltip(tooltip) {
        if (typeof tooltip.hidePopover === 'function') {
            if (tooltip.matches(':popover-open')) {
                tooltip.hidePopover();
            }

            return;
        }

        tooltip.style.display = 'none';
    }

    function cancelSessionTooltipHide(tooltip) {
        const state = sessionTooltipStateByElement.get(tooltip);
        if (state?.hideTimerId !== undefined) {
            window.clearTimeout(state.hideTimerId);
            state.hideTimerId = undefined;
        }
    }

    function scheduleSessionTooltipHide(tooltip) {
        const state = sessionTooltipStateByElement.get(tooltip);
        if (!state) {
            closeSessionTooltip(tooltip);
            return;
        }

        cancelSessionTooltipHide(tooltip);
        state.hideTimerId = window.setTimeout(() => {
            state.hideTimerId = undefined;
            if (tooltip.matches(':hover') || state.anchor?.matches(':hover')) {
                return;
            }

            closeSessionTooltip(tooltip);
        }, 150);
    }

    function ensureSessionTooltipState(tooltip) {
        if (sessionTooltipStateByElement.has(tooltip)) {
            return sessionTooltipStateByElement.get(tooltip);
        }

        const state = { anchor: undefined, hideTimerId: undefined };
        sessionTooltipStateByElement.set(tooltip, state);
        tooltip.addEventListener('mouseenter', () => cancelSessionTooltipHide(tooltip));
        tooltip.addEventListener('mouseleave', () => scheduleSessionTooltipHide(tooltip));
        return state;
    }

    cockpit.showSessionTooltip = (anchor, tooltip) => {
        if (!(anchor instanceof HTMLElement) || !(tooltip instanceof HTMLElement)) {
            return;
        }

        const state = ensureSessionTooltipState(tooltip);
        state.anchor = anchor;
        cancelSessionTooltipHide(tooltip);
        if (typeof tooltip.showPopover === 'function') {
            tooltip.style.removeProperty('display');
            if (!tooltip.matches(':popover-open')) {
                tooltip.showPopover();
            }
        } else {
            tooltip.style.display = 'block';
        }

        const margin = 6;
        const anchorRect = anchor.getBoundingClientRect();
        const tooltipRect = tooltip.getBoundingClientRect();
        let left = anchorRect.right + margin;
        if (left + tooltipRect.width > window.innerWidth - margin) {
            left = Math.max(margin, anchorRect.left - tooltipRect.width - margin);
        }

        const top = Math.min(
            Math.max(margin, anchorRect.top),
            Math.max(margin, window.innerHeight - tooltipRect.height - margin));
        tooltip.style.left = `${left}px`;
        tooltip.style.top = `${top}px`;
    };

    cockpit.hideSessionTooltip = (tooltip) => {
        if (!(tooltip instanceof HTMLElement)) {
            return;
        }

        scheduleSessionTooltipHide(tooltip);
    };

    cockpit.cleanupSessionListLoadMore = () => {
        sessionListLoadMoreObserver?.disconnect();
        sessionListLoadMoreObserver = null;
    };

    cockpit.observeSessionListLoadMore = (sentinel, dotNetReference) => {
        cockpit.cleanupSessionListLoadMore();
        if (!(sentinel instanceof HTMLElement) || !dotNetReference) {
            return;
        }

        const scrollContainer = sentinel.closest('[data-session-scroll-container]');
        sessionListLoadMoreObserver = new IntersectionObserver((entries) => {
            if (!entries.some(entry => entry.isIntersecting)) {
                return;
            }

            sessionListLoadMoreObserver?.disconnect();
            sessionListLoadMoreObserver = null;
            dotNetReference.invokeMethodAsync('LoadMoreRecents').catch(() => {});
        }, {
            root: scrollContainer,
            rootMargin: '200px 0px',
            threshold: 0
        });
        sessionListLoadMoreObserver.observe(sentinel);
    };
})();
