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

    highlightDiffCells: function (containerId, language) {
        if (!window.hljs) return;
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
            requestAnimationFrame(() => { syncing = false; });
        };
        right._splitScrollHandler = () => {
            if (syncing) return;
            syncing = true;
            left.scrollLeft = right.scrollLeft;
            left.scrollTop = right.scrollTop;
            requestAnimationFrame(() => { syncing = false; });
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
    },

    // -------------------------------------------------------------------------
    // ContentEditable methods for the chat input div
    // -------------------------------------------------------------------------

    setupContentEditable: function (id, dotnetRef) {
        const element = document.getElementById(id);
        if (!element) return;

        // Clean up any existing observer/listener
        if (element._ceObserver) {
            element._ceObserver.disconnect();
            delete element._ceObserver;
        }
        if (element._ceInputHandler) {
            element.removeEventListener('input', element._ceInputHandler);
            delete element._ceInputHandler;
        }

        element._ceRef = dotnetRef;
        element._suppressChipRemovalCallback = false;

        // Click delegation: handle chip × delete button
        element._ceClickHandler = function (event) {
            const deleteBtn = event.target.closest && event.target.closest('.chip-delete');
            if (deleteBtn) {
                const chip = deleteBtn.closest('.file-mention-chip');
                if (chip) {
                    event.preventDefault();
                    event.stopPropagation();
                    // Remove adjacent zero-width spacer after chip
                    let next = chip.nextSibling;
                    while (next && next.nodeType === 3 && next.textContent.startsWith('\u200B')) {
                        const toRemove = next;
                        next = next.nextSibling;
                        toRemove.remove();
                    }
                    chip.remove();
                    window.cockpit.autoResizeContentEditable(id);
                }
            }
        };
        element.addEventListener('click', element._ceClickHandler);

        // Input event → notify C#
        element._ceInputHandler = function () {
            dotnetRef.invokeMethodAsync('OnContentInput').catch(() => {});
        };
        element.addEventListener('input', element._ceInputHandler);

        // MutationObserver to detect chip removal
        const observer = new MutationObserver(function (mutations) {
            for (const mutation of mutations) {
                for (const node of mutation.removedNodes) {
                    if (node.nodeType === 1 && node.classList && node.classList.contains('file-mention-chip')) {
                        if (!element._suppressChipRemovalCallback) {
                            const chipId = node.dataset.chipId;
                            if (chipId) {
                                dotnetRef.invokeMethodAsync('OnChipRemoved', chipId).catch(() => {});
                            }
                        }
                    }
                }
            }
        });
        observer.observe(element, { childList: true, subtree: true });
        element._ceObserver = observer;
    },

    getActiveMentionFilter: function (id) {
        const element = document.getElementById(id);
        if (!element) return null;

        const sel = window.getSelection();
        if (!sel || sel.rangeCount === 0) return null;
        const range = sel.getRangeAt(0);
        if (!range.collapsed) return null;

        // If cursor is inside a chip, return null
        const node = range.startContainer;
        if (node.nodeType === 1 && node.classList && node.classList.contains('file-mention-chip')) return null;
        if (node.nodeType === 3 && node.parentElement && node.parentElement.closest('.file-mention-chip')) return null;

        // Must be a text node inside our div
        if (node.nodeType !== 3) return null;
        if (!element.contains(node)) return null;

        const textBeforeCursor = node.textContent.substring(0, range.startOffset);
        const hashIndex = textBeforeCursor.lastIndexOf('#');
        if (hashIndex === -1) return null;

        const afterHash = textBeforeCursor.substring(hashIndex + 1);

        // If there's any whitespace between # and cursor → not an active mention
        if (/\s/.test(afterHash)) return null;

        // If # is preceded by a non-whitespace character → mid-word #, not a trigger
        if (hashIndex > 0 && /\S/.test(textBeforeCursor[hashIndex - 1])) return null;

        // Save the range so insertFileChip can use it even if focus moves to the picker
        element._savedMentionRange = range.cloneRange();

        return afterHash;
    },

    insertFileChip: function (id, chipId, filePath, fileName) {
        const element = document.getElementById(id);
        if (!element) return;

        // Prefer the range saved by getActiveMentionFilter (reliable even when focus moves to picker)
        let range;
        if (element._savedMentionRange) {
            range = element._savedMentionRange;
            element._savedMentionRange = null;
        } else {
            const sel = window.getSelection();
            if (!sel || sel.rangeCount === 0) return;
            range = sel.getRangeAt(0);
        }

        // Find and delete the #<filter> text before cursor in the current text node
        if (range.startContainer.nodeType === 3) {
            const textNode = range.startContainer;
            const offset = range.startOffset;
            const textBefore = textNode.textContent.substring(0, offset);
            const hashIndex = textBefore.lastIndexOf('#');
            if (hashIndex !== -1) {
                // Delete from # to cursor
                const deleteRange = document.createRange();
                deleteRange.setStart(textNode, hashIndex);
                deleteRange.setEnd(textNode, offset);
                deleteRange.deleteContents();
                // Update range after deletion
                range.setStart(textNode, hashIndex);
                range.collapse(true);
            }
        }

        // Build chip span
        const chip = document.createElement('span');
        chip.className = 'file-mention-chip';
        chip.contentEditable = 'false';
        chip.dataset.chipId = chipId;
        chip.dataset.filePath = filePath;
        chip.title = filePath;

        // SVG icon (constant markup, no user input)
        chip.innerHTML =
            '<svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
            '<path d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"/>' +
            '</svg>';

        // File name span (uses textContent to avoid HTML injection)
        const nameSpan = document.createElement('span');
        nameSpan.className = 'chip-name';
        nameSpan.textContent = ' ' + fileName;
        chip.appendChild(nameSpan);

        // Delete button
        const deleteButton = document.createElement('button');
        deleteButton.className = 'chip-delete';
        deleteButton.tabIndex = -1;
        deleteButton.title = 'Remove';
        deleteButton.textContent = '×';
        chip.appendChild(deleteButton);
        // Insert chip at cursor
        range.insertNode(chip);

        // Insert zero-width space + space after chip
        const spacer = document.createTextNode('\u200B ');
        chip.after(spacer);

        // Move cursor after the space (offset 2: past ZWS + space)
        const curSel = window.getSelection();
        if (curSel) {
            const newRange = document.createRange();
            newRange.setStart(spacer, 2);
            newRange.collapse(true);
            curSel.removeAllRanges();
            curSel.addRange(newRange);
        }

        // Trigger resize
        window.cockpit.autoResizeContentEditable(id);
    },

    removeFileChip: function (id, chipId) {
        const element = document.getElementById(id);
        if (!element) return;

        const chip = element.querySelector('[data-chip-id="' + chipId + '"]');
        if (!chip) return;

        element._suppressChipRemovalCallback = true;

        // Remove any immediately adjacent zero-width space text nodes after the chip
        let next = chip.nextSibling;
        while (next && next.nodeType === 3 && next.textContent.startsWith('\u200B')) {
            const toRemove = next;
            next = next.nextSibling;
            toRemove.remove();
        }

        chip.remove();

        element._suppressChipRemovalCallback = false;

        window.cockpit.autoResizeContentEditable(id);
    },

    getPlainText: function (id) {
        const element = document.getElementById(id);
        if (!element) return '';

        function walk(node, isFirstChild) {
            if (node.nodeType === 3) {
                // Text node — return as-is
                return node.textContent;
            }
            if (node.nodeType !== 1) return '';

            if (node.classList && node.classList.contains('file-mention-chip')) {
                return "#file:'" + node.dataset.filePath + "'";
            }

            const tag = node.tagName ? node.tagName.toUpperCase() : '';

            if (tag === 'BR') return '\n';

            let prefix = '';
            if ((tag === 'DIV' || tag === 'P') && !isFirstChild) {
                prefix = '\n';
            }

            let inner = '';
            let childIndex = 0;
            for (const child of node.childNodes) {
                inner += walk(child, childIndex === 0);
                childIndex++;
            }

            return prefix + inner;
        }

        let result = '';
        let childIndex = 0;
        for (const child of element.childNodes) {
            result += walk(child, childIndex === 0);
            childIndex++;
        }

        // Strip leading/trailing zero-width spaces
        result = result.replace(/^\u200B+/, '').replace(/\u200B+$/, '');
        // Trim trailing whitespace
        result = result.replace(/\s+$/, '');

        return result;
    },

    setPlainText: function (id, text) {
        const element = document.getElementById(id);
        if (!element) return [];

        element._suppressChipRemovalCallback = true;
        element.innerHTML = '';

        const chipRegex = /#file:'([^']*)'/g;
        const chips = [];
        let lastIndex = 0;
        let match;

        while ((match = chipRegex.exec(text)) !== null) {
            // Text before this token
            if (match.index > lastIndex) {
                element.appendChild(document.createTextNode(text.substring(lastIndex, match.index)));
            }

            const filePath = match[1];
            const fileName = filePath.split(/[\\/]/).pop();
            const newChipId = (typeof crypto !== 'undefined' && crypto.randomUUID)
                ? crypto.randomUUID()
                : Math.random().toString(36).slice(2);

            const chip = document.createElement('span');
            chip.className = 'file-mention-chip';
            chip.contentEditable = 'false';
            chip.dataset.chipId = newChipId;
            chip.dataset.filePath = filePath;
            chip.title = filePath;
            chip.innerHTML =
                '<svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                '<path d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"/>' +
                '</svg>' +
                '<span class="chip-name"> ' + fileName + '</span>' +
                '<button class="chip-delete" tabindex="-1" title="Remove">&#215;</button>';

            element.appendChild(chip);

            // Spacer after chip
            element.appendChild(document.createTextNode('\u200B '));

            chips.push({ chipId: newChipId, filePath: filePath });
            lastIndex = match.index + match[0].length;
        }

        // Remaining text after last token
        if (lastIndex < text.length) {
            element.appendChild(document.createTextNode(text.substring(lastIndex)));
        }

        element._suppressChipRemovalCallback = false;

        window.cockpit.autoResizeContentEditable(id);

        return chips;
    },

    getChipIds: function (id) {
        const div = document.getElementById(id);
        if (!div) return [];
        return [...div.querySelectorAll('.file-mention-chip')].map(s => ({
            chipId: s.dataset.chipId,
            filePath: s.dataset.filePath
        }));
    },

    scrollPickerItemIntoView: function (id) {
        const el = document.getElementById(id);
        if (el) el.scrollIntoView({ block: 'nearest' });
    },

    autoResizeContentEditable: function (id) {
        const el = document.getElementById(id);
        if (!el) return;
        el.style.height = 'auto';
        const maxHeight = 300;
        el.style.height = Math.min(el.scrollHeight, maxHeight) + 'px';
    },

    setupContentEditableBehavior: function (id, enterToSend) {
        const element = document.getElementById(id);
        if (!element) return;

        element.dataset.enterToSend = enterToSend;

        // Remove existing keydown handler if present
        if (element._ceKeydownHandler) {
            element.removeEventListener('keydown', element._ceKeydownHandler);
            delete element._ceKeydownHandler;
        }

        element._ceKeydownHandler = function (e) {
            // When the mention picker is active, prevent the browser from acting on navigation keys.
            // ArrowDown/Up would move the cursor away from the #filter position;
            // Enter would insert a newline. C# handles all of these via @onkeydown.
            if (e.key === 'Enter' || e.key === 'ArrowDown' || e.key === 'ArrowUp' || e.key === 'Escape') {
                if (window.cockpit.getActiveMentionFilter(id) !== null) {
                    e.preventDefault();
                    return;
                }
            }

            if (e.key === 'Enter') {
                if (!e.shiftKey && element.dataset.enterToSend === 'true') {
                    // Send mode: prevent default (C# handles send via keydown)
                    e.preventDefault();
                } else {
                    // Newline mode: prevent default, insert <br> manually
                    e.preventDefault();
                    const sel = window.getSelection();
                    if (!sel || sel.rangeCount === 0) return;
                    const range = sel.getRangeAt(0);
                    range.deleteContents();

                    const br = document.createElement('br');
                    range.insertNode(br);

                    // Insert a zero-width space after <br> to keep cursor visible
                    const spacer = document.createTextNode('\u200B');
                    br.after(spacer);

                    const newRange = document.createRange();
                    newRange.setStartAfter(br);
                    newRange.collapse(true);
                    sel.removeAllRanges();
                    sel.addRange(newRange);

                    window.cockpit.autoResizeContentEditable(id);
                }
            }

            // Single-press chip removal: Backspace/Delete adjacent to a chip
            if (e.key === 'Backspace' || e.key === 'Delete') {
                const sel = window.getSelection();
                if (!sel || sel.rangeCount === 0 || !sel.getRangeAt(0).collapsed) return;
                const range = sel.getRangeAt(0);
                const node = range.startContainer;
                const offset = range.startOffset;

                if (e.key === 'Backspace') {
                    // Cursor is in the ZWS spacer text node immediately following a chip
                    if (node.nodeType === 3 &&
                        node.previousSibling &&
                        node.previousSibling.nodeType === 1 &&
                        node.previousSibling.classList.contains('file-mention-chip')) {
                        const zwsLen = (node.textContent.match(/^\u200B+/) || [''])[0].length;
                        if (offset <= zwsLen) {
                            e.preventDefault();
                            const chip = node.previousSibling;
                            node.remove();
                            chip.remove(); // MutationObserver fires OnChipRemoved
                            window.cockpit.autoResizeContentEditable(id);
                            return;
                        }
                    }
                    // Cursor is at offset 0 in a node whose previous sibling is a chip
                    if (offset === 0 &&
                        node.previousSibling &&
                        node.previousSibling.nodeType === 1 &&
                        node.previousSibling.classList.contains('file-mention-chip')) {
                        e.preventDefault();
                        node.previousSibling.remove();
                        window.cockpit.autoResizeContentEditable(id);
                        return;
                    }
                }

                if (e.key === 'Delete') {
                    // Cursor is at the end of a text node whose next sibling is a chip
                    let nextNode = null;
                    if (node.nodeType === 3 && offset === node.textContent.length) {
                        nextNode = node.nextSibling;
                    } else if (node.nodeType === 1) {
                        nextNode = node.childNodes[offset] || null;
                    }
                    if (nextNode &&
                        nextNode.nodeType === 1 &&
                        nextNode.classList.contains('file-mention-chip')) {
                        e.preventDefault();
                        let spacer = nextNode.nextSibling;
                        while (spacer && spacer.nodeType === 3 && spacer.textContent.startsWith('\u200B')) {
                            const toRemove = spacer;
                            spacer = spacer.nextSibling;
                            toRemove.remove();
                        }
                        nextNode.remove();
                        window.cockpit.autoResizeContentEditable(id);
                        return;
                    }
                }
            }
        };

        element.addEventListener('keydown', element._ceKeydownHandler);
    },

    cleanupContentEditable: function (id) {
        const el = document.getElementById(id);
        if (!el) return;
        if (el._ceObserver) { el._ceObserver.disconnect(); delete el._ceObserver; }
        if (el._ceInputHandler) { el.removeEventListener('input', el._ceInputHandler); delete el._ceInputHandler; }
        if (el._ceKeydownHandler) { el.removeEventListener('keydown', el._ceKeydownHandler); delete el._ceKeydownHandler; }
        if (el._ceClickHandler) { el.removeEventListener('click', el._ceClickHandler); delete el._ceClickHandler; }
        delete el._ceRef;
        delete el._suppressChipRemovalCallback;
        delete el._savedMentionRange;
    }
};

// Isolate links and buttons inside Blazor clickable containers so they don't bubble up
// to parent onclick handlers (e.g. event-message popup, tool-summary expander).
(function () {
    const ISOLATED = ['.event-message', '.tool-summary', '.thinking-message', '.working-message'];

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

// Global function to toggle settings from MAUI title bar
window.toggleSettings = function () {
    if (window._mainLayoutRef) {
        window._mainLayoutRef.invokeMethodAsync('ToggleSettingsFromTitleBar')
            .catch(err => console.error('Failed to toggle settings:', err));
    }
};