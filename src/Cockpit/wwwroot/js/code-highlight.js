window.cockpit ??= {};

(() => {
    const copyFeedbackDurationMs = 1500;

    const selectors = Object.freeze({
        codeBlock: '.code-block',
        copyButton: '.code-copy-button',
        code: 'pre code',
        unhighlightedCode: 'pre code:not([data-highlighted])'
    });

    const buttonLabels = Object.freeze({
        default: 'Copy',
        success: 'Copied',
        failure: 'Failed'
    });
    const copyFeedbackTimers = new WeakMap();
    const copyButtonsWithHandler = new WeakSet();

    function getElementById(elementId) {
        return typeof elementId === 'string' && elementId.length > 0
            ? document.getElementById(elementId)
            : null;
    }

    function getHighlighter() {
        const { hljs } = window;
        return hljs && typeof hljs.highlightElement === 'function' ? hljs : null;
    }

    function clearCopyFeedbackTimer(button) {
        const timerId = copyFeedbackTimers.get(button);
        if (timerId === undefined) {
            return;
        }

        window.clearTimeout(timerId);
        copyFeedbackTimers.delete(button);
    }

    function setCopyButtonFeedback(button, label, isSuccess) {
        clearCopyFeedbackTimer(button);
        button.textContent = label;
        button.classList.toggle('copied', isSuccess);

        const timerId = window.setTimeout(() => {
            copyFeedbackTimers.delete(button);
            if (!button.isConnected) {
                return;
            }

            button.textContent = buttonLabels.default;
            button.classList.remove('copied');
        }, copyFeedbackDurationMs);

        copyFeedbackTimers.set(button, timerId);
    }

    function highlightElements(elements) {
        const highlighter = getHighlighter();
        if (!highlighter) {
            return;
        }

        for (const element of elements) {
            if (!(element instanceof HTMLElement) || element.dataset.highlighted) {
                continue;
            }

            try {
                highlighter.highlightElement(element);
            } catch (error) {
                console.warn('Failed to highlight code element.', error, element);
            }
        }
    }

    function getCodeElementForButton(button) {
        return button.closest(selectors.codeBlock)?.querySelector(selectors.code) ?? null;
    }

    function restoreSelection(selection, ranges) {
        if (!selection) {
            return;
        }

        try {
            selection.removeAllRanges();
            for (const range of ranges) {
                selection.addRange(range);
            }
        } catch {
            selection.removeAllRanges();
        }
    }

    function copyTextWithExecCommand(text) {
        if (!document.body || typeof document.execCommand !== 'function') {
            throw new Error('No clipboard fallback is available.');
        }

        const selection = window.getSelection();
        const previousRanges = [];
        if (selection) {
            for (let index = 0; index < selection.rangeCount; index += 1) {
                previousRanges.push(selection.getRangeAt(index).cloneRange());
            }
        }

        const activeElement = document.activeElement instanceof HTMLElement
            ? document.activeElement
            : null;

        const textArea = document.createElement('textarea');
        textArea.value = text;
        textArea.setAttribute('readonly', 'readonly');
        textArea.setAttribute('aria-hidden', 'true');
        Object.assign(textArea.style, {
            position: 'fixed',
            top: '0',
            left: '0',
            width: '1px',
            height: '1px',
            opacity: '0',
            pointerEvents: 'none'
        });

        document.body.append(textArea);

        try {
            textArea.select();
            textArea.setSelectionRange(0, textArea.value.length);

            if (!document.execCommand('copy')) {
                throw new Error('document.execCommand("copy") returned false.');
            }
        } finally {
            textArea.remove();
            restoreSelection(selection, previousRanges);
            activeElement?.focus();
        }
    }

    async function writeClipboardText(text) {
        if (navigator.clipboard?.writeText) {
            try {
                await navigator.clipboard.writeText(text);
                return;
            } catch {
                copyTextWithExecCommand(text);
                return;
            }
        }

        copyTextWithExecCommand(text);
    }

    async function copyCode(button) {
        const codeElement = getCodeElementForButton(button);
        if (!(codeElement instanceof HTMLElement)) {
            setCopyButtonFeedback(button, buttonLabels.failure, false);
            return;
        }

        try {
            await writeClipboardText(codeElement.textContent ?? '');
            setCopyButtonFeedback(button, buttonLabels.success, true);
        } catch (error) {
            console.warn('Failed to copy code to the clipboard.', error, button);
            setCopyButtonFeedback(button, buttonLabels.failure, false);
        }
    }

    function handleCopyButtonClick(event) {
        const button = event.currentTarget;
        if (!(button instanceof HTMLButtonElement) || button.disabled) {
            return;
        }

        event.preventDefault();
        event.stopPropagation();
        void copyCode(button);
    }

    function wireCopyButtons(container) {
        for (const button of container.querySelectorAll(selectors.copyButton)) {
            if (!(button instanceof HTMLButtonElement) || copyButtonsWithHandler.has(button)) {
                continue;
            }

            button.addEventListener('click', handleCopyButtonClick);
            copyButtonsWithHandler.add(button);
        }
    }

    window.cockpit.highlightCodeBlocks = function (containerId) {
        const container = getElementById(containerId);
        if (!container) {
            return;
        }

        highlightElements(container.querySelectorAll(selectors.unhighlightedCode));
    };

    window.cockpit.highlightBlock = function (elementId) {
        const element = getElementById(elementId);
        if (!element) {
            return;
        }

        highlightElements([element]);
    };

    window.cockpit.addCopyButtonsToCodeBlocks = function (containerId) {
        const container = getElementById(containerId);
        if (!container) {
            return;
        }

        wireCopyButtons(container);
    };

})();
