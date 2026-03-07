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
    setMainLayoutRef: function (dotNetRef) {
        window._mainLayoutRef = dotNetRef;
    },
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
        if (element._smartScrollClickHandler) {
            element.removeEventListener('click', element._smartScrollClickHandler, { capture: true });
        }
        if (element._smartScrollResizeObserver) {
            element._smartScrollResizeObserver.disconnect();
        }
        if (element._smartScrollMutationObserver) {
            element._smartScrollMutationObserver.disconnect();
        }
        clearTimeout(element._smartScrollClickTimeout);

        const checkState = function (fromUserScroll) {
            const isNearBottom = element.scrollHeight - element.scrollTop - element.clientHeight < 50;
            if (element._wasNearBottom !== isNearBottom) {
                // Content growth should maintain the auto-scroll anchor — UNLESS the user just
                // clicked something (e.g. expanding a tool), in which case respect their position.
                if (!isNearBottom && !fromUserScroll) {
                    if (element._recentClick) {
                        element._wasNearBottom = false;
                        dotnetHelper.invokeMethodAsync(methodName, false);
                    } else {
                        element.scrollTop = element.scrollHeight;
                    }
                    return;
                }
                element._wasNearBottom = isNearBottom;
                dotnetHelper.invokeMethodAsync(methodName, isNearBottom);
            }
        };

        element._smartScrollHandler = () => checkState(true);
        element.addEventListener('scroll', element._smartScrollHandler);

        // Track clicks so we can distinguish user-triggered expansions from pure content growth.
        element._recentClick = false;
        element._smartScrollClickHandler = () => {
            element._recentClick = true;
            clearTimeout(element._smartScrollClickTimeout);
            element._smartScrollClickTimeout = setTimeout(() => { element._recentClick = false; }, 500);
        };
        element.addEventListener('click', element._smartScrollClickHandler, { capture: true });

        // Also recheck when content inside grows (tool rows expanding, new messages)
        element._smartScrollResizeObserver = new ResizeObserver(() => checkState(false));
        // Observe all direct children so any expansion triggers a recheck
        const observeChildren = () => {
            for (const child of element.children) {
                element._smartScrollResizeObserver.observe(child);
            }
        };
        observeChildren();

        // Watch for new children AND text content changes (streaming tokens into existing nodes)
        element._smartScrollMutationObserver = new MutationObserver(() => {
            observeChildren();
            if (element._wasNearBottom) {
                if (element._recentClick) {
                    // User-triggered DOM change — check actual position rather than forcing scroll.
                    const isNearBottom = element.scrollHeight - element.scrollTop - element.clientHeight < 50;
                    if (!isNearBottom) {
                        element._wasNearBottom = false;
                        dotnetHelper.invokeMethodAsync(methodName, false);
                    }
                } else {
                    // Pure content growth — keep pinned to bottom.
                    element.scrollTop = element.scrollHeight;
                }
            } else {
                checkState(false);
            }
        });
        element._smartScrollMutationObserver.observe(element, { childList: true, subtree: true, characterData: true });

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
        if (element._smartScrollClickHandler) {
            element.removeEventListener('click', element._smartScrollClickHandler, { capture: true });
            delete element._smartScrollClickHandler;
        }
        clearTimeout(element._smartScrollClickTimeout);
        delete element._smartScrollClickTimeout;
        delete element._recentClick;
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

        container.querySelectorAll('.code-block').forEach((block) => {
            const button = block.querySelector('.code-copy-button');
            const pre = block.querySelector('pre');
            if (!button || !pre || button.dataset.initialized) {
                return;
            }

            button.dataset.initialized = 'true';
            pre.classList.add('scrollbar-thin');

            const code = pre.querySelector('code');
            if (!code) {
                return;
            }

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
    },

    highlightDiffCells: function (containerId, language) {        if (!window.hljs) return;
        const container = document.getElementById(containerId);
        if (!container) return;

        const lang = hljs.getLanguage(language) ? language : 'plaintext';

        container.querySelectorAll('td.diff-cell').forEach(cell => {
            // Preserve the ± prefix span (inline view only)
            const prefix = cell.querySelector('.diff-prefix');
            const prefixHtml = prefix ? prefix.outerHTML : '';
            const rawText = prefix
                ? cell.textContent.substring(prefix.textContent.length)
                : cell.textContent;

            try {
                const result = hljs.highlight(rawText, { language: lang, ignoreIllegals: true });
                cell.innerHTML = prefixHtml + result.value;
            } catch (_) { /* ignore */ }
        });
    },

    setupSplitDiffScroll: function (leftId, rightId) {
        const left = document.getElementById(leftId);
        const right = document.getElementById(rightId);
        if (!left || !right) return;

        // Clean up any previous listeners on these elements
        if (left._splitScrollHandler) left.removeEventListener('scroll', left._splitScrollHandler);
        if (right._splitScrollHandler) right.removeEventListener('scroll', right._splitScrollHandler);

        let syncing = false;

        left._splitScrollHandler = () => {
            if (syncing) return;
            syncing = true;
            right.scrollLeft = left.scrollLeft;
            right.scrollTop = left.scrollTop;
            syncing = false;
        };
        right._splitScrollHandler = () => {
            if (syncing) return;
            syncing = true;
            left.scrollLeft = right.scrollLeft;
            left.scrollTop = right.scrollTop;
            syncing = false;
        };

        left.addEventListener('scroll', left._splitScrollHandler);
        right.addEventListener('scroll', right._splitScrollHandler);
    },

    openDialog: function (element) {
        if (element && !element.open) {
            element.showModal();
        }
    },

    closeDialog: function (element) {
        if (element && element.open) {
            element.close();
        }
    },

    setupImagePaste: function (inputId, dotnetRef) {
        const element = document.getElementById(inputId);
        if (!element) return;

        if (element._pasteHandler) {
            element.removeEventListener('paste', element._pasteHandler);
        }

        element._pasteHandler = function (e) {
            const items = e.clipboardData?.items;
            if (!items) return;

            // Find the first usable image item before handling it
            let imageItem = null;
            let imageFile = null;

            for (const item of items) {
                if (!item.type.startsWith('image/')) {
                    continue;
                }

                const file = item.getAsFile();
                if (!file) {
                    continue;
                }

                imageItem = item;
                imageFile = file;
                break; // only first image
            }

            if (!imageItem || !imageFile) {
                // No image found; allow default paste behavior
                return;
            }

            e.preventDefault();

            const ext = imageItem.type.split('/')[1]?.replace('jpeg', 'jpg') ?? 'png';
            const fileName = `pasted-image.${ext}`;

            const reader = new FileReader();
            reader.onload = function (ev) {
                const dataUrl = ev.target.result;
                // strip the "data:image/xxx;base64," prefix
                const base64 = dataUrl.split(',')[1];
                dotnetRef.invokeMethodAsync('OnImagePasted', base64, imageItem.type, ext, fileName)
                    .catch(err => console.error('OnImagePasted failed:', err));
            };
            reader.readAsDataURL(imageFile);
        };

        element.addEventListener('paste', element._pasteHandler);
    },

    cleanupImagePaste: function (inputId) {
        const element = document.getElementById(inputId);
        if (!element) return;
        if (element._pasteHandler) {
            element.removeEventListener('paste', element._pasteHandler);
            delete element._pasteHandler;
        }
    },

    showImageLightbox: function (src, alt) {
        let overlay = document.getElementById('_cockpit_lightbox');
        if (!overlay) {
            overlay = document.createElement('div');
            overlay.id = '_cockpit_lightbox';
            overlay.style.cssText = 'position:fixed;inset:0;z-index:9999;background:rgba(0,0,0,0.85);display:flex;align-items:center;justify-content:center;cursor:zoom-out;';

            const closeLightbox = () => {
                overlay.remove();
                document.removeEventListener('keydown', keyHandler);
            };

            overlay.addEventListener('click', (event) => {
                if (event.target === overlay) {
                    closeLightbox();
                }
            });

            const img = document.createElement('img');
            img.id = '_cockpit_lightbox_img';
            img.style.cssText = 'max-width:90vw;max-height:90vh;object-fit:contain;border-radius:6px;box-shadow:0 8px 40px rgba(0,0,0,0.6);';
            overlay.appendChild(img);

            // close on Escape
            const keyHandler = (e) => { if (e.key === 'Escape') closeLightbox(); };
            document.addEventListener('keydown', keyHandler);
        }
        overlay.querySelector('#_cockpit_lightbox_img').src = src;
        overlay.querySelector('#_cockpit_lightbox_img').alt = alt ?? '';
        document.body.appendChild(overlay);
    }
};

// Isolate links and buttons inside Blazor clickable containers so they don't bubble up
// to parent onclick handlers (e.g. event-message popup, tool-summary expander).
(function () {
    const ISOLATED = ['.event-message', '.tool-summary', '.thinking-message.cursor-pointer'];

    function attachIsolation(el) {
        if (el._clickIsolated) return;
        el._clickIsolated = true;
        el.addEventListener('click', function (e) { e.stopPropagation(); });
    }

    function scanAndIsolate(root) {
        if (!root.querySelectorAll) return;
        var selector = ISOLATED.map(function (s) { return s + ' a, ' + s + ' button'; }).join(', ');
        root.querySelectorAll(selector).forEach(attachIsolation);
    }

    var observer = new MutationObserver(function (mutations) {
        mutations.forEach(function (mutation) {
            mutation.addedNodes.forEach(function (node) {
                if (node.nodeType !== 1) return;
                scanAndIsolate(node);
                if (node.tagName === 'A' || node.tagName === 'BUTTON') {
                    if (ISOLATED.some(function (s) { return node.closest && node.closest(s); })) {
                        attachIsolation(node);
                    }
                }
            });
        });
    });

    function start() {
        observer.observe(document.body, { childList: true, subtree: true });
    }

    if (document.body) {
        start();
    } else {
        document.addEventListener('DOMContentLoaded', start);
    }
})();

// Suppress clicks on expander headers when the user has selected text (e.g. dragging to highlight).
// Reads getSelection() at mouseup time (most reliable — selection is cleared before click fires),
// and combines with mouse-movement check as fallback.
(function () {
    const EXPANDERS = ['.group-header', '.tool-summary', '.thinking-message.cursor-pointer'];

    var _downX = 0, _downY = 0;
    var _hadSelection = false;

    // Reset on every new press
    document.addEventListener('mousedown', function (e) {
        if (e.button !== 0) return;
        _downX = e.clientX;
        _downY = e.clientY;
        _hadSelection = false;
    }, { capture: true });

    // Capture selection state at mouseup — selection is still present here
    document.addEventListener('mouseup', function (e) {
        if (e.button !== 0) return;
        var sel = window.getSelection && window.getSelection();
        _hadSelection = !!(sel && sel.toString().length > 0);
    }, { capture: true });

    document.addEventListener('click', function (e) {
        var dx = e.clientX - _downX;
        var dy = e.clientY - _downY;
        var moved = Math.sqrt(dx * dx + dy * dy) > 4;

        if (!_hadSelection && !moved) return;
        _hadSelection = false;

        var t = e.target;
        if (!t || !t.closest) return;
        for (var i = 0; i < EXPANDERS.length; i++) {
            if (t.closest(EXPANDERS[i])) {
                e.stopImmediatePropagation();
                return;
            }
        }
    }, { capture: true });
})();

// Global function to toggle settings from MAUI title bar
window.toggleSettings = function () {
    if (window._mainLayoutRef) {
        window._mainLayoutRef.invokeMethodAsync('ToggleSettingsFromTitleBar')
            .catch(err => console.error('Failed to toggle settings:', err));
    }
};
