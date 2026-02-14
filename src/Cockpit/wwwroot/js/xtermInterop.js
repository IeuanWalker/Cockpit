window.xtermInterop = window.xtermInterop || {};

window.xtermInterop.triggerResize = function (terminalElementId) {
	const termEl = document.getElementById(terminalElementId);
	if (!termEl || !termEl.xterm) {
		return;
	}

	// Call fit to resize the terminal - this handles the reflow
	const fitAddon = termEl.xterm._fitAddon;
	if (fitAddon && fitAddon.fit) {
		fitAddon.fit();
	}

	// Force a complete repaint by clearing the texture atlas
	// This ensures no stale rendering artifacts remain
	if (termEl.xterm.clearTextureAtlas) {
		termEl.xterm.clearTextureAtlas();
	}
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
	const observer = new ResizeObserver(entries => {
		for (const entry of entries) {
			if (entry.target === termEl) {
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

window.xtermInterop.clearTerminal = function (terminalElementId) {
	const termEl = document.getElementById(terminalElementId);
	if (termEl && termEl.xterm && termEl.xterm.clear) {
		termEl.xterm.clear();
	}
};

window.xtermInterop.registerWindowResize = function (terminalElementId, dotnetHelper) {
	function onResize() {
		window.xtermInterop.triggerResize(terminalElementId);
		dotnetHelper.invokeMethodAsync('OnTerminalWindowResize');
	}
	window.addEventListener('resize', onResize);
	return {
		dispose: () => window.removeEventListener('resize', onResize)
	};
};

