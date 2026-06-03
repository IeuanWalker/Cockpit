(() => {
	const cockpit = window.cockpit ??= {};
	const executableScriptTypes = new Set(["", "text/javascript", "application/javascript", "text/ecmascript", "application/ecmascript"]);
	const libraryLoads = new Map();

	cockpit.canvas = {
		/// Sets the innerHTML of the container, lazy-loads local canvas libraries,
		/// re-executes JavaScript <script> tags in order, and auto-renders Mermaid blocks.
		setContent: async function (container, html) {
			if (!container) return;

			await ensureCanvasLibrariesAsync();
			container.innerHTML = html ?? '';

			for (const oldScript of getExecutableScripts(container)) {
				await new Promise((resolve) => {
					const newScript = document.createElement('script');
					for (const attr of oldScript.attributes) {
						newScript.setAttribute(attr.name, attr.value);
					}
					newScript.textContent = oldScript.textContent;

					const srcAttr = oldScript.getAttribute('src');
					if (srcAttr) {
						newScript.onload = resolve;
						newScript.onerror = resolve;
					}

					oldScript.replaceWith(newScript);

					if (!srcAttr) resolve();
				});
			}

			await renderMermaidAsync(container);
		}
	};

	function getExecutableScripts(container) {
		return Array.from(container.querySelectorAll('script'))
			.filter((script) => executableScriptTypes.has((script.getAttribute('type') ?? '').trim().toLowerCase()));
	}

	function loadScriptOnceAsync(src) {
		if (libraryLoads.has(src)) {
			return libraryLoads.get(src);
		}

		const promise = new Promise((resolve, reject) => {
			const existing = document.querySelector(`script[data-canvas-lib="${src}"]`);
			if (existing) {
				if (existing.dataset.loaded === 'true') {
					resolve();
					return;
				}

				existing.addEventListener('load', () => resolve(), { once: true });
				existing.addEventListener('error', () => reject(new Error(`Failed to load ${src}`)), { once: true });
				return;
			}

			const script = document.createElement('script');
			script.src = src;
			script.async = false;
			script.dataset.canvasLib = src;
			script.onload = () => {
				script.dataset.loaded = 'true';
				resolve();
			};
			script.onerror = () => reject(new Error(`Failed to load ${src}`));
			document.head.appendChild(script);
		});

		libraryLoads.set(src, promise);
		return promise;
	}

	async function ensureCanvasLibrariesAsync() {
		await Promise.all([
			loadScriptOnceAsync('js/vendor/chart.umd.min.js'),
			loadScriptOnceAsync('js/vendor/mermaid.min.js')
		]);

		if (window.mermaid?.initialize) {
			const styles = getComputedStyle(document.documentElement);
			const isLightTheme = document.body.classList.contains('light-theme');
			window.mermaid.initialize({
				startOnLoad: false,
				securityLevel: 'loose',
				theme: 'base',
				themeVariables: {
					darkMode: !isLightTheme,
					background: styles.getPropertyValue('--bg-color').trim(),
					mainBkg: styles.getPropertyValue('--bg-color').trim(),
					secondBkg: styles.getPropertyValue('--sidebar-color').trim(),
					tertiaryColor: styles.getPropertyValue('--hover-color').trim(),
					primaryColor: styles.getPropertyValue('--accent-color').trim(),
					primaryBorderColor: styles.getPropertyValue('--border-color').trim(),
					primaryTextColor: styles.getPropertyValue('--title-color').trim(),
					secondaryTextColor: styles.getPropertyValue('--text-color').trim(),
					lineColor: styles.getPropertyValue('--text-color').trim()
				}
			});
		}
	}

	function replaceWithMermaidBlock(node, diagramText) {
		const block = document.createElement('div');
		block.className = 'mermaid';
		block.textContent = (diagramText ?? '').trim();
		node.replaceWith(block);
	}

	function normalizeMermaidBlocks(container) {
		for (const node of container.querySelectorAll('script[type="text/mermaid"]')) {
			replaceWithMermaidBlock(node, node.textContent);
		}

		for (const node of container.querySelectorAll('pre.mermaid')) {
			replaceWithMermaidBlock(node, node.textContent);
		}

		for (const node of container.querySelectorAll('pre code.language-mermaid, pre code.lang-mermaid, pre code.mermaid, code.language-mermaid, code.lang-mermaid, code.mermaid')) {
			const target = node.parentElement?.tagName === 'PRE' ? node.parentElement : node;
			replaceWithMermaidBlock(target, node.textContent);
		}
	}

	async function renderMermaidAsync(container) {
		if (!window.mermaid) {
			return;
		}

		normalizeMermaidBlocks(container);

		const nodes = Array.from(container.querySelectorAll('.mermaid'));
		if (nodes.length === 0) {
			return;
		}

		if (typeof window.mermaid.run === 'function') {
			await window.mermaid.run({ nodes });
			return;
		}

		if (typeof window.mermaid.init === 'function') {
			window.mermaid.init(undefined, nodes);
		}
	}
})();
