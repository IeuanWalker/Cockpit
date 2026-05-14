/*
 * C# conversion plan:
 * 1. Keep these interop functions in JavaScript for now: syntax highlighting depends on the browser-loaded
 *    highlight.js instance (`window.hljs`), and copy support depends on the browser-only Clipboard API.
 * 2. If server-side highlighting is pursued, prototype a Markdown/C# pipeline that renders highlighted HTML
 *    for both MarkdownRenderer and CodeBlock, then compare output, startup cost, and rerender behavior with
 *    the current highlight.js path before switching callers away from JS interop.
 * 3. Introduce any future C# highlighter behind the existing component/service boundaries so the Blazor
 *    components can choose pre-highlighted HTML or the current JS fallback without changing call sites.
 * 4. Keep a minimal JS bridge for copy-to-clipboard even if highlighting moves to C#, because `navigator.clipboard`
 *    remains browser-specific inside the WebView.
 * 5. Validate fenced code blocks, large snippets, repeated renders, theme changes, and `hljs`-missing fallback
 *    before removing this file or the highlight.js dependency.
 */
window.cockpit = window.cockpit || {};

(() => {
    const copyFeedbackDurationMs = 1500;
    const scrollbarClass = 'scrollbar-thin';

    const selectors = Object.freeze({
        codeBlock: '.code-block',
        copyButton: '.code-copy-button',
        code: 'pre code',
        unhighlightedCode: 'pre code:not([data-highlighted])',
        scrollablePre: `.code-block pre:not(.${scrollbarClass})`
    });

    const buttonLabels = Object.freeze({
        default: 'Copy',
        success: 'Copied',
        failure: 'Failed'
    });
    const copyFeedbackTimers = new WeakMap();
    const containersWithCopyHandler = new WeakSet();

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

    function addScrollbarStyling(container) {
        for (const preElement of container.querySelectorAll(selectors.scrollablePre)) {
            preElement.classList.add(scrollbarClass);
        }
    }

    function getCopyButtonFromEvent(eventTarget, container) {
        const sourceElement = eventTarget instanceof Element
            ? eventTarget
            : eventTarget instanceof Node
                ? eventTarget.parentElement
                : null;

        if (!sourceElement) {
            return null;
        }

        const button = sourceElement.closest(selectors.copyButton);
        return button instanceof HTMLButtonElement && container.contains(button)
            ? button
            : null;
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
        const container = event.currentTarget;
        if (!(container instanceof HTMLElement)) {
            return;
        }

        const button = getCopyButtonFromEvent(event.target, container);
        if (!button || button.disabled) {
            return;
        }

        event.preventDefault();
        void copyCode(button);
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

        addScrollbarStyling(container);

        if (containersWithCopyHandler.has(container)) {
            return;
        }

        container.addEventListener('click', handleCopyButtonClick);
        containersWithCopyHandler.add(container);
    };

})();
