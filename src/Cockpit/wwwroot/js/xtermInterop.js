window.xtermInterop = window.xtermInterop || {};
console.log('[xtermInterop] script loaded');

window.xtermInterop.triggerResize = function(terminalElementId) {
    console.log('[xtermInterop] triggerResize CALLED', terminalElementId);
    const termEl = document.getElementById(terminalElementId);
    if (termEl && termEl.xterm && termEl.xterm._core && termEl.xterm._core._onResize) {
        // Try to trigger a resize event if possible
        termEl.xterm.resize(termEl.offsetWidth, termEl.offsetHeight);
    }
    if (termEl && termEl.xterm && termEl.xterm.fit) {
        termEl.xterm.fit();
    }
};

window.xtermInterop.getTerminalSize = function (terminalElementId) {
    const termEl = document.getElementById(terminalElementId);
    if (!termEl || !termEl.xterm) {
        console.warn('[xtermInterop] getTerminalSize missing terminal', terminalElementId);
        return null;
    }
    const scrollbarWidth = 14;
    let containerWidth = termEl.offsetWidth;
    // Detect if vertical scrollbar is present
    const hasVScroll = termEl.scrollHeight > termEl.clientHeight;
    if (hasVScroll) {
        containerWidth -= scrollbarWidth;
    }
    const cellWidth = termEl.xterm._core._renderService.dimensions.actualCellWidth;
    const cellHeight = termEl.xterm._core._renderService.dimensions.actualCellHeight;
    let cols = Math.floor(containerWidth / cellWidth);
    let rows = Math.floor(termEl.offsetHeight / cellHeight);
    console.log('[xtermInterop] getTerminalSize', {
        terminalElementId,
        containerWidth,
        cellWidth,
        cellHeight,
        cols,
        rows,
        offsetWidth: termEl.offsetWidth,
        offsetHeight: termEl.offsetHeight,
        scrollHeight: termEl.scrollHeight,
        clientHeight: termEl.clientHeight,
        hasVScroll
    });
    // Fallback to xterm's own cols/rows if calculation fails
    if (!isFinite(cols) || cols <= 0) cols = termEl.xterm.cols;
    if (!isFinite(rows) || rows <= 0) rows = termEl.xterm.rows;
    return {
        cols: cols,
        rows: rows
    };
};

window.xtermInterop.observeElementResize = function(terminalElementId, dotnetHelper) {
    const termEl = document.getElementById(terminalElementId);
    if (!termEl) {
        console.warn('[xtermInterop] observeElementResize missing terminal element', terminalElementId);
        return { dispose: () => {} };
    }
    console.log('[xtermInterop] observeElementResize CALLED', terminalElementId);
    const observer = new ResizeObserver(entries => {
        for (const entry of entries) {
            if (entry.target === termEl) {
                console.log('[xtermInterop] element resize detected', {
                    terminalElementId,
                    contentRect: entry.contentRect
                });
                window.xtermInterop.triggerResize(terminalElementId);
                dotnetHelper.invokeMethodAsync('OnTerminalWindowResize');
            }
        }
    });
    observer.observe(termEl);
    return {
        dispose: () => observer.disconnect()
    };
};

window.xtermInterop.registerWindowResize = function(terminalElementId, dotnetHelper) {
    console.log('[xtermInterop] registerWindowResize CALLED', terminalElementId);
    function onResize() {
        console.log('[xtermInterop] onResize CALLED', {
            terminalElementId,
            offsetWidth: document.getElementById(terminalElementId)?.offsetWidth,
            offsetHeight: document.getElementById(terminalElementId)?.offsetHeight,
            innerWidth: window.innerWidth,
            innerHeight: window.innerHeight
        });
        window.xtermInterop.triggerResize(terminalElementId);
        dotnetHelper.invokeMethodAsync('OnTerminalWindowResize');
    }
    window.addEventListener('resize', onResize);
    return {
        dispose: () => window.removeEventListener('resize', onResize)
    };
};

