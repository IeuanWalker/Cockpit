window.xtermInterop = window.xtermInterop || {};

window.xtermInterop.triggerResize = function(terminalElementId) {
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
    if (!termEl || !termEl.xterm) return null;
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
    // Fallback to xterm's own cols/rows if calculation fails
    if (!isFinite(cols) || cols <= 0) cols = termEl.xterm.cols;
    if (!isFinite(rows) || rows <= 0) rows = termEl.xterm.rows;
    return {
        cols: cols,
        rows: rows
    };
};

window.xtermInterop.registerWindowResize = function(terminalElementId, dotnetHelper) {
    function onResize() {
        window.xtermInterop.triggerResize(terminalElementId);
        dotnetHelper.invokeMethodAsync('OnTerminalWindowResize');
    }
    window.addEventListener('resize', onResize);
    return {
        dispose: () => window.removeEventListener('resize', onResize)
    };
};

