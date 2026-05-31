(() => {
	const cockpit = window.cockpit ??= {};

	cockpit.canvas = {
		/// Sets the innerHTML of the container and re-executes all <script> tags in order.
		/// External <script src="..."> tags are awaited (onload/onerror) before the next
		/// script runs, so patterns like: cdn load → use library work correctly.
		setContent: async function (container, html) {
			if (!container) return;
			container.innerHTML = html ?? '';

			for (const oldScript of container.querySelectorAll('script')) {
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
		}
	};
})();
