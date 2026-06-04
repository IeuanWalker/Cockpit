(() => {
	const cockpit = window.cockpit ??= {};
	let _libs = null;

	cockpit.canvas = {
		/// Renders agent-provided HTML inside a sandboxed <iframe>.
		/// sandbox="allow-scripts" permits JS execution (Chart.js, Mermaid, Tailwind)
		/// but blocks access to the parent window, cookies, storage, navigation,
		/// popups, forms, and modals.
		setContent: async function (container, html) {
			if (!container) return;

			const libs = await loadLibrariesAsync();
			const themeVars = readThemeVariables();
			const isLightTheme = document.body.classList.contains('light-theme');
			const srcdoc = buildSandboxedDocument(html ?? '', libs, themeVars, isLightTheme);

			let iframe = container.querySelector('iframe.canvas-sandbox');
			if (!iframe) {
				iframe = document.createElement('iframe');
				iframe.className = 'canvas-sandbox';
				iframe.sandbox = 'allow-scripts';
				iframe.style.cssText = 'width:100%;height:100%;border:none;display:block;';
				container.innerHTML = '';
				container.appendChild(iframe);
			}

			iframe.srcdoc = srcdoc;
		}
	};

	// ── Theme variable bridge ─────────────────────────────────────────────
	const THEME_VARS = [
		'--bg-color', '--text-color', '--title-color', '--secondary-text',
		'--accent-color', '--border-color', '--sidebar-color', '--hover-color'
	];

	function readThemeVariables() {
		const styles = getComputedStyle(document.documentElement);
		return THEME_VARS
			.map(v => `${v}: ${styles.getPropertyValue(v).trim() || 'unset'};`)
			.join(' ');
	}

	// ── Local library loader ──────────────────────────────────────────────
	// Fetches app CSS and JS libraries from the host WebView's local origin
	// and caches them for the lifetime of the page. Re-read on app restart.
	async function loadLibrariesAsync() {
		if (_libs) return _libs;

		const [appCss, tailwindJs, chartJs, mermaidJs] = await Promise.all([
			fetchTextAsync('css/app.css'),
			fetchTextAsync('js/Tailwind.js'),
			fetchTextAsync('js/vendor/chart.umd.min.js'),
			fetchTextAsync('js/vendor/mermaid.min.js')
		]);

		_libs = { appCss, tailwindJs, chartJs, mermaidJs };
		return _libs;
	}

	async function fetchTextAsync(path) {
		try {
			const res = await fetch(path);
			if (!res.ok) {
				console.error(`[canvas] Failed to load ${path}: HTTP ${res.status}`);
				return '';
			}
			return await res.text();
		} catch (err) {
			console.error(`[canvas] Error fetching ${path}:`, err);
			return '';
		}
	}

	// ── Srcdoc builder ────────────────────────────────────────────────────
	// The HTML parser treats the first </script> it finds as the closing tag,
	// even inside JS string literals. Replace with <\/script which is a
	// harmless no-op escape in JavaScript but invisible to the HTML parser.
	function escapeScriptClose(text) {
		if (!text) return '';
		return text.replace(/<\/script/gi, '<\\/script');
	}

	function buildSandboxedDocument(html, libs, cssVarOverrides, isLightTheme) {
		const safeTailwind = escapeScriptClose(libs.tailwindJs);
		const safeChart = escapeScriptClose(libs.chartJs);
		const safeMermaid = escapeScriptClose(libs.mermaidJs);

		return `<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<style>${libs.appCss}</style>
<style>:root { ${cssVarOverrides} }
canvas { display: block; max-width: 100%; }
</style>
<script>${safeTailwind}</script>
<script>${safeChart}</script>
<script>
if (window.Chart) {
	// Force maintainAspectRatio so agent-provided charts can't grow infinitely.
	// Without a fixed-height parent, responsive + !maintainAspectRatio causes
	// an infinite resize loop. Patch the constructor to enforce this.
	Chart.defaults.responsive = true;
	Chart.defaults.maintainAspectRatio = true;
	var _OrigChart = Chart;
	var _origCtor = _OrigChart.prototype.constructor;
	window.Chart = function (ctx, config) {
		if (config && config.options) {
			config.options.maintainAspectRatio = true;
		}
		return new _OrigChart(ctx, config);
	};
	window.Chart.prototype = _OrigChart.prototype;
	Object.setPrototypeOf(window.Chart, _OrigChart);
	// Copy static properties (defaults, register, etc.)
	for (var k in _OrigChart) {
		if (_OrigChart.hasOwnProperty(k)) {
			window.Chart[k] = _OrigChart[k];
		}
	}
}
</script>
<script>${safeMermaid}</script>
</head>
<body class="canvas-sandbox-body${isLightTheme ? ' light-theme' : ''}">
${html}
<script>
(async function () {
	var SELECTORS =
		'.mermaid, pre.mermaid, script[type="text/mermaid"],' +
		'pre code.language-mermaid, pre code.lang-mermaid, pre code.mermaid,' +
		'code.language-mermaid, code.lang-mermaid, code.mermaid';
	if (!document.querySelector(SELECTORS)) return;
	if (!window.mermaid || !window.mermaid.initialize) return;

	var s = getComputedStyle(document.documentElement);
	var isLight = document.body.classList.contains('light-theme');
	window.mermaid.initialize({
		startOnLoad: false,
		securityLevel: 'strict',
		theme: 'base',
		themeVariables: {
			darkMode: !isLight,
			background:         s.getPropertyValue('--bg-color').trim(),
			mainBkg:            s.getPropertyValue('--bg-color').trim(),
			secondBkg:          s.getPropertyValue('--sidebar-color').trim(),
			tertiaryColor:      s.getPropertyValue('--hover-color').trim(),
			primaryColor:       s.getPropertyValue('--accent-color').trim(),
			primaryBorderColor: s.getPropertyValue('--border-color').trim(),
			primaryTextColor:   s.getPropertyValue('--title-color').trim(),
			secondaryTextColor: s.getPropertyValue('--text-color').trim(),
			lineColor:          s.getPropertyValue('--text-color').trim()
		}
	});

	document.querySelectorAll('script[type="text/mermaid"]').forEach(function (n) { _r(n, n.textContent); });
	document.querySelectorAll('pre.mermaid').forEach(function (n) { _r(n, n.textContent); });
	document.querySelectorAll(
		'pre code.language-mermaid, pre code.lang-mermaid, pre code.mermaid,' +
		'code.language-mermaid, code.lang-mermaid, code.mermaid'
	).forEach(function (n) {
		var t = n.parentElement && n.parentElement.tagName === 'PRE' ? n.parentElement : n;
		_r(t, n.textContent);
	});

	function _r(node, text) {
		var d = document.createElement('div');
		d.className = 'mermaid';
		d.textContent = (text || '').trim();
		node.replaceWith(d);
	}

	var nodes = Array.from(document.querySelectorAll('.mermaid'));
	if (nodes.length === 0) return;
	if (typeof window.mermaid.run === 'function') {
		await window.mermaid.run({ nodes: nodes });
	} else if (typeof window.mermaid.init === 'function') {
		window.mermaid.init(undefined, nodes);
	}
})();
</script>
</body>
</html>`;
	}
})();
