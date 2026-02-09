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
    addCopyButtonsToCodeBlocks: function (containerId) {
        const container = document.getElementById(containerId);
        if (!container) {
            return;
        }

        container.querySelectorAll('pre').forEach((pre) => {
            if (pre.querySelector('.code-copy-button')) {
                return;
            }

            const code = pre.querySelector('code');
            if (!code) {
                return;
            }

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

            pre.classList.add('code-block');
            pre.appendChild(button);
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
    }
};

// Global function to toggle settings from MAUI title bar
window.toggleSettings = function () {
    // Call the .NET static method
    DotNet.invokeMethodAsync('Cockpit', 'ToggleSettingsFromTitleBar')
        .catch(err => console.error('Failed to toggle settings:', err));
};
