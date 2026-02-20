window.localStorageHelper = {
    getItem: function (key) {
        return localStorage.getItem(key);
    },
    setItem: function (key, value) {
        localStorage.setItem(key, value);
    },
    removeItem: function (key) {
        localStorage.removeItem(key);
    }
};

window.cockpit = {
    setRootProperty: function (property, value) {
        document.documentElement.style.setProperty(property, value);
    },
    setAccentColor: function (color, hoverColor) {
        document.documentElement.style.setProperty('--accent-color', color);
        document.documentElement.style.setProperty('--button-bg', color);
        document.documentElement.style.setProperty('--button-hover', hoverColor);
    },
    addBodyClass: function (className) {
        document.body.classList.add(className);
    },
    removeBodyClass: function (className) {
        document.body.classList.remove(className);
    },
    focusElement: function (elementId) {
        const element = document.getElementById(elementId);
        if (element) {
            element.focus();
        }
    },
    autoResizeTextarea: function (elementId) {
        const element = document.getElementById(elementId);
        if (element) {
            element.style.height = 'auto';
            element.style.height = Math.min(element.scrollHeight, 300) + 'px';
        }
    },
    setupChatInputBehavior: function (elementId, enterToSend) {
        const element = document.getElementById(elementId);
        if (!element) return;

        element.dataset.enterToSend = enterToSend;

        // Remove existing listener if any
        if (element._keypressHandler) {
            element.removeEventListener('keypress', element._keypressHandler);
        }

        // Add new listener
        element._keypressHandler = function (e) {
            if (e.key === 'Enter' && !e.shiftKey && element.dataset.enterToSend === 'true') {
                e.preventDefault();
            }
        };

        element.addEventListener('keypress', element._keypressHandler);
    },
    scrollToBottom: function (elementId) {
        const element = document.getElementById(elementId);
        if (element) {
            element.scrollTop = element.scrollHeight;
        }
    },
    setupSmartScroll: function (elementId, dotnetHelper, methodName) {
        const element = document.getElementById(elementId);
        if (!element) return;

        // Remove existing listeners if any
        if (element._smartScrollHandler) {
            element.removeEventListener('scroll', element._smartScrollHandler);
        }
        if (element._smartScrollResizeObserver) {
            element._smartScrollResizeObserver.disconnect();
        }

        const checkState = function () {
            const isNearBottom = element.scrollHeight - element.scrollTop - element.clientHeight < 50;
            if (element._wasNearBottom !== isNearBottom) {
                element._wasNearBottom = isNearBottom;
                dotnetHelper.invokeMethodAsync(methodName, isNearBottom);
            }
        };

        element._smartScrollHandler = checkState;
        element.addEventListener('scroll', element._smartScrollHandler);

        // Also recheck when content inside grows (tool rows expanding, new messages)
        element._smartScrollResizeObserver = new ResizeObserver(checkState);
        // Observe all direct children so any expansion triggers a recheck
        const observeChildren = () => {
            for (const child of element.children) {
                element._smartScrollResizeObserver.observe(child);
            }
        };
        observeChildren();

        // Watch for new children being added
        element._smartScrollMutationObserver = new MutationObserver(() => {
            observeChildren();
            checkState();
        });
        element._smartScrollMutationObserver.observe(element, { childList: true });

        // Initialize state
        const isNearBottom = element.scrollHeight - element.scrollTop - element.clientHeight < 50;
        element._wasNearBottom = isNearBottom;
    },
    cleanupSmartScroll: function (elementId) {
        const element = document.getElementById(elementId);
        if (!element) return;
        if (element._smartScrollHandler) {
            element.removeEventListener('scroll', element._smartScrollHandler);
            delete element._smartScrollHandler;
        }
        if (element._smartScrollResizeObserver) {
            element._smartScrollResizeObserver.disconnect();
            delete element._smartScrollResizeObserver;
        }
        if (element._smartScrollMutationObserver) {
            element._smartScrollMutationObserver.disconnect();
            delete element._smartScrollMutationObserver;
        }
        delete element._wasNearBottom;
    },
    highlightCodeBlocks: function (containerId) {
        if (!window.hljs) {
            return;
        }

        const container = document.getElementById(containerId);
        if (!container) {
            return;
        }

        container.querySelectorAll('pre code').forEach((block) => {
            window.hljs.highlightElement(block);
        });
    },
    highlightBlock: function (elementId) {
        if (!window.hljs) return;
        const element = document.getElementById(elementId);
        if (element) {
            window.hljs.highlightElement(element);
        }
    },
    addCopyButtonsToCodeBlocks: function (containerId) {
        const container = document.getElementById(containerId);
        if (!container) {
            return;
        }

        container.querySelectorAll('pre').forEach((pre) => {
            if (pre.parentNode.classList.contains('code-block')) {
                return;
            }

            const code = pre.querySelector('code');
            if (!code) {
                return;
            }

            // Wrap pre in a positioned container so the copy button doesn't scroll with code
            const wrapper = document.createElement('div');
            wrapper.className = 'code-block';
            pre.parentNode.insertBefore(wrapper, pre);
            wrapper.appendChild(pre);

            // Apply thin scrollbar styling to the pre
            pre.classList.add('scrollbar-thin');

            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'code-copy-button';
            button.textContent = 'Copy';
            button.addEventListener('click', async () => {
                try {
                    await navigator.clipboard.writeText(code.innerText);
                    button.textContent = 'Copied';
                    button.classList.add('copied');
                    setTimeout(() => {
                        button.textContent = 'Copy';
                        button.classList.remove('copied');
                    }, 1500);
                } catch {
                    button.textContent = 'Failed';
                    setTimeout(() => {
                        button.textContent = 'Copy';
                    }, 1500);
                }
            });

            wrapper.appendChild(button);
        });
    },
    initializeResize: function (handleId, sidebarId, side, dotnetHelper) {
        const handle = document.getElementById(handleId);
        const sidebar = document.getElementById(sidebarId);

        if (!handle || !sidebar) return;

        let isResizing = false;

        const startResize = (e) => {
            e.preventDefault();
            isResizing = true;
            handle.classList.add('resizing');
            document.body.style.cursor = 'col-resize';
            document.body.style.userSelect = 'none';

            const doResize = (e) => {
                if (!isResizing) return;
                const newWidth = side === 'left'
                    ? Math.max(150, Math.min(600, e.clientX))
                    : Math.max(150, Math.min(600, window.innerWidth - e.clientX));

                sidebar.style.width = newWidth + 'px';
                dotnetHelper.invokeMethodAsync('OnResize', newWidth);
            };

            const stopResize = () => {
                if (!isResizing) return;
                isResizing = false;
                handle.classList.remove('resizing');
                document.body.style.cursor = '';
                document.body.style.userSelect = '';

                document.removeEventListener('mousemove', doResize);
                document.removeEventListener('mouseup', stopResize);
            };

            document.addEventListener('mousemove', doResize);
            document.addEventListener('mouseup', stopResize);
        };

        handle.addEventListener('mousedown', startResize);
    },
    setupScrollAnchor: function (elementId) {
        const element = document.getElementById(elementId);
        if (!element) return;

        // Clean up any existing observer
        if (element._resizeObserver) {
            element._resizeObserver.disconnect();
        }

        let lastHeight = element.clientHeight;

        element._resizeObserver = new ResizeObserver(() => {
            const newHeight = element.clientHeight;
            const delta = lastHeight - newHeight; // positive = element shrank
            if (delta > 0) {
                // Panel expanded below us — push scroll down to keep same content visible
                element.scrollTop += delta;
            }
            lastHeight = newHeight;
        });

        element._resizeObserver.observe(element);
    },
    cleanupScrollAnchor: function (elementId) {
        const element = document.getElementById(elementId);
        if (element && element._resizeObserver) {
            element._resizeObserver.disconnect();
            delete element._resizeObserver;
        }
    }
};

// Global function to toggle settings from MAUI title bar
window.toggleSettings = function () {
    // Call the .NET static method
    DotNet.invokeMethodAsync('Cockpit', 'ToggleSettingsFromTitleBar')
        .catch(err => console.error('Failed to toggle settings:', err));
};
