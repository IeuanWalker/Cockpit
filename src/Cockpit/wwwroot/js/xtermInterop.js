window.xtermInterop = window.xtermInterop || {};

window.xtermInterop.triggerResize = function (terminalElementId) {
    const termEl = document.getElementById(terminalElementId);
    if (!termEl || !termEl.xterm) {
        return;
    }

    const term = termEl.xterm;
    // Anchor the viewport to the bottom of the active screen so scrollback
    // content pulled into view during a large resize doesn't stay visible.
    // Without this, `clear` only clears the active buffer while the scrollback
    // remains visible in the newly revealed rows.
    term.scrollToBottom?.();
    if (term.clearTextureAtlas) {
        term.clearTextureAtlas();
    }
    // Defer refresh to the next animation frame so it runs after xterm's own
    // resize render cycle, ensuring all rows are cleanly repainted.
    requestAnimationFrame(() => {
        if (term.refresh) {
            term.refresh(0, term.rows - 1);
        }
    });
};

window.xtermInterop.getTerminalSize = function (terminalElementId) {
    const termEl = document.getElementById(terminalElementId);
    if (!termEl || !termEl.xterm) {
        return null;
    }

    const cols = termEl.xterm.cols;
    const rows = termEl.xterm.rows;

    if (!Number.isFinite(cols) || cols <= 0 || !Number.isFinite(rows) || rows <= 0) {
        return null;
    }

    return {
        cols: cols,
        rows: rows
    };
};

window.xtermInterop.observeElementResize = function (terminalElementId, dotnetHelper) {
    const termEl = document.getElementById(terminalElementId);
    if (!termEl) {
        return { dispose: () => { } };
    }
    // Observe the container (parent) so the terminal canvas size adjustments don't re-trigger
    const target = termEl.parentElement ?? termEl;
    let debounceTimer = null;
    const observer = new ResizeObserver(() => {
        clearTimeout(debounceTimer);
        debounceTimer = setTimeout(() => {
            dotnetHelper.invokeMethodAsync('OnTerminalWindowResize');
        }, 50);
    });
    observer.observe(target);
    return {
        dispose: () => {
            clearTimeout(debounceTimer);
            observer.disconnect();
        }
    };
};

window.xtermInterop.clearTerminal = function (terminalElementId) {
    const termEl = document.getElementById(terminalElementId);
    if (termEl && termEl.xterm && termEl.xterm.clear) {
        termEl.xterm.clear();
    }
};

window.xtermInterop.registerWindowResize = function (terminalElementId, dotnetHelper) {
    let debounceTimer = null;
    function onResize() {
        clearTimeout(debounceTimer);
        debounceTimer = setTimeout(() => {
            dotnetHelper.invokeMethodAsync('OnTerminalWindowResize');
        }, 50);
    }
    window.addEventListener('resize', onResize);
    return {
        dispose: () => {
            clearTimeout(debounceTimer);
            window.removeEventListener('resize', onResize);
        }
    };
};

