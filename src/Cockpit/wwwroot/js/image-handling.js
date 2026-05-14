window.cockpit = window.cockpit || {};

(() => {
    const INPUT_EVENT_NAME = 'input';
    const IMAGE_PASTE_METHOD_NAME = 'OnImagePasted';
    const LIGHTBOX_ID = '_cockpit_lightbox';
    const LIGHTBOX_TITLE_ID = '_cockpit_lightbox_title';
    const LIGHTBOX_DESCRIPTION_ID = '_cockpit_lightbox_description';
    const LIGHTBOX_IMAGE_ID = '_cockpit_lightbox_image';
    const OVERLAY_STYLE = 'position:fixed;inset:0;z-index:9999;padding:24px;background:rgba(0,0,0,0.85);display:flex;align-items:center;justify-content:center;cursor:zoom-out;';
    const IMAGE_STYLE = 'max-width:90vw;max-height:90vh;object-fit:contain;border-radius:6px;box-shadow:0 8px 40px rgba(0,0,0,0.6);cursor:default;';
    const CLOSE_BUTTON_STYLE = 'position:absolute;top:16px;right:16px;min-width:44px;min-height:44px;padding:8px 14px;border:0;border-radius:999px;background:rgba(0,0,0,0.55);color:#fff;font:inherit;cursor:pointer;';
    const VISUALLY_HIDDEN_STYLE = 'position:absolute;width:1px;height:1px;padding:0;margin:-1px;overflow:hidden;clip:rect(0,0,0,0);clip-path:inset(50%);white-space:nowrap;border:0;';
    const pasteHandlerRegistrations = new WeakMap();
    let lightboxController = null;

    function getElement(elementId) {
        return document.getElementById(elementId);
    }

    function createElement(tagName, options = {}) {
        const {
            parent = null,
            textContent = null,
            styleText = '',
            attributes = {},
            properties = {}
        } = options;
        const element = document.createElement(tagName);

        if (styleText) {
            element.style.cssText = styleText;
        }

        if (textContent !== null) {
            element.textContent = textContent;
        }

        for (const [name, value] of Object.entries(attributes)) {
            if (value !== null && value !== undefined) {
                element.setAttribute(name, String(value));
            }
        }

        Object.assign(element, properties);

        if (parent) {
            parent.append(element);
        }

        return element;
    }

    function logInteropError(message, error) {
        console.error(message, error instanceof Error ? error : new Error(String(error)));
    }

    function dispatchInputEvent(element) {
        element.dispatchEvent(new Event(INPUT_EVENT_NAME, { bubbles: true }));
    }

    function isSelectionInsideElement(element, range) {
        return element.contains(range.commonAncestorContainer);
    }

    function tryInsertTextWithExecCommand(text) {
        if (typeof document.execCommand !== 'function') {
            return false;
        }

        try {
            return document.execCommand('insertText', false, text);
        } catch {
            return false;
        }
    }

    function tryInsertTextWithRange(element, text) {
        const selection = window.getSelection();
        if (!selection || selection.rangeCount === 0) {
            return false;
        }

        const range = selection.getRangeAt(0);
        if (!isSelectionInsideElement(element, range)) {
            return false;
        }

        range.deleteContents();

        const textNode = document.createTextNode(text);
        range.insertNode(textNode);
        range.setStartAfter(textNode);
        range.collapse(true);

        selection.removeAllRanges();
        selection.addRange(range);
        return true;
    }

    function insertPlainText(element, text) {
        if (tryInsertTextWithExecCommand(text)) {
            return;
        }

        if (tryInsertTextWithRange(element, text)) {
            dispatchInputEvent(element);
            return;
        }

        element.append(document.createTextNode(text));
        dispatchInputEvent(element);
    }

    function getPastedImage(clipboardData) {
        for (const item of clipboardData?.items ?? []) {
            if (!item.type?.startsWith('image/')) {
                continue;
            }

            const file = item.getAsFile();
            if (file) {
                return {
                    file,
                    mimeType: item.type
                };
            }
        }

        return null;
    }

    function getImageExtension(mimeType) {
        return mimeType.split('/')[1]?.replace('jpeg', 'jpg') ?? 'png';
    }

    function readFileAsDataUrl(file) {
        return new Promise((resolve, reject) => {
            const reader = new FileReader();

            reader.onload = () => {
                if (typeof reader.result === 'string') {
                    resolve(reader.result);
                    return;
                }

                reject(new Error('Pasted image did not produce a data URL.'));
            };

            reader.onerror = () => reject(reader.error ?? new Error('Failed to read pasted image.'));
            reader.onabort = () => reject(new Error('Pasted image read was aborted.'));
            reader.readAsDataURL(file);
        });
    }

    function extractBase64Payload(dataUrl) {
        const delimiterIndex = dataUrl.indexOf(',');
        return delimiterIndex >= 0 ? dataUrl.slice(delimiterIndex + 1) : '';
    }

    async function notifyDotNetOfPastedImage(dotnetRef, pastedImage) {
        if (!dotnetRef || typeof dotnetRef.invokeMethodAsync !== 'function') {
            throw new Error('A valid .NET interop reference is required for image paste handling.');
        }

        const extension = getImageExtension(pastedImage.mimeType);
        const fileName = `pasted-image.${extension}`;
        const dataUrl = await readFileAsDataUrl(pastedImage.file);
        const base64Payload = extractBase64Payload(dataUrl);

        if (!base64Payload) {
            throw new Error('Pasted image did not contain base64 data.');
        }

        await dotnetRef.invokeMethodAsync(
            IMAGE_PASTE_METHOD_NAME,
            base64Payload,
            pastedImage.mimeType,
            extension,
            fileName);
    }

    function consumePlainTextPaste(event, element) {
        const plainText = event.clipboardData?.getData('text/plain');
        if (!plainText) {
            return false;
        }

        event.preventDefault();
        insertPlainText(element, plainText);
        return true;
    }

    async function handlePasteEvent(event, element, dotnetRef) {
        const clipboardData = event.clipboardData;
        if (!clipboardData) {
            return;
        }

        const pastedImage = getPastedImage(clipboardData);
        if (pastedImage) {
            event.preventDefault();
            await notifyDotNetOfPastedImage(dotnetRef, pastedImage);
            return;
        }

        consumePlainTextPaste(event, element);
    }

    function detachPasteHandler(element) {
        if (!element) {
            return;
        }

        const registration = pasteHandlerRegistrations.get(element);
        if (!registration) {
            return;
        }

        element.removeEventListener('paste', registration.onPaste);
        pasteHandlerRegistrations.delete(element);
    }

    function attachPasteHandler(element, dotnetRef) {
        detachPasteHandler(element);

        const registration = {
            onPaste: async (event) => {
                try {
                    await handlePasteEvent(event, element, dotnetRef);
                } catch (error) {
                    logInteropError('Failed to handle paste event.', error);
                }
            }
        };

        pasteHandlerRegistrations.set(element, registration);
        element.addEventListener('paste', registration.onPaste);
    }

    function createLightboxController() {
        let overlay = null;
        let image = null;
        let closeButton = null;
        let previouslyFocusedElement = null;
        let previousBodyOverflow = '';

        function hide() {
            if (!overlay) {
                return;
            }

            overlay.setAttribute('aria-hidden', 'true');
            image.removeAttribute('src');

            if (overlay.isConnected) {
                overlay.remove();
                document.removeEventListener('keydown', handleDocumentKeyDown);
                document.body.style.overflow = previousBodyOverflow;
            }

            previouslyFocusedElement?.focus?.();
            previouslyFocusedElement = null;
        }

        function handleOverlayClick(event) {
            if (event.target === overlay) {
                hide();
            }
        }

        function getFocusableElements() {
            if (!overlay) {
                return [];
            }

            return [...overlay.querySelectorAll('button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])')]
                .filter((element) => !element.hasAttribute('aria-hidden'));
        }

        function trapFocus(event) {
            if (event.key !== 'Tab') {
                return;
            }

            const focusableElements = getFocusableElements();
            if (focusableElements.length === 0) {
                event.preventDefault();
                overlay.focus();
                return;
            }

            const firstFocusableElement = focusableElements[0];
            const lastFocusableElement = focusableElements[focusableElements.length - 1];
            const activeElement = document.activeElement;

            if (!overlay.contains(activeElement)) {
                event.preventDefault();
                firstFocusableElement.focus();
                return;
            }

            if (event.shiftKey && activeElement === firstFocusableElement) {
                event.preventDefault();
                lastFocusableElement.focus();
                return;
            }

            if (!event.shiftKey && activeElement === lastFocusableElement) {
                event.preventDefault();
                firstFocusableElement.focus();
            }
        }

        function handleDocumentKeyDown(event) {
            if (event.key === 'Escape') {
                event.preventDefault();
                hide();
                return;
            }

            trapFocus(event);
        }

        function ensureElements() {
            if (overlay) {
                return;
            }

            overlay = createElement('div', {
                styleText: OVERLAY_STYLE,
                attributes: {
                    role: 'dialog',
                    'aria-modal': 'true',
                    'aria-hidden': 'true',
                    'aria-labelledby': LIGHTBOX_TITLE_ID,
                    'aria-describedby': LIGHTBOX_DESCRIPTION_ID
                },
                properties: {
                    id: LIGHTBOX_ID,
                    tabIndex: -1
                }
            });

            overlay.addEventListener('click', handleOverlayClick);

            createElement('h2', {
                parent: overlay,
                textContent: 'Image preview',
                styleText: VISUALLY_HIDDEN_STYLE,
                properties: {
                    id: LIGHTBOX_TITLE_ID
                }
            });

            createElement('p', {
                parent: overlay,
                textContent: 'Press Escape or activate Close to dismiss the image preview.',
                styleText: VISUALLY_HIDDEN_STYLE,
                properties: {
                    id: LIGHTBOX_DESCRIPTION_ID
                }
            });

            closeButton = createElement('button', {
                parent: overlay,
                textContent: 'Close',
                styleText: CLOSE_BUTTON_STYLE,
                attributes: {
                    'aria-label': 'Close image preview'
                },
                properties: {
                    type: 'button'
                }
            });
            closeButton.addEventListener('click', hide);

            image = createElement('img', {
                parent: overlay,
                styleText: IMAGE_STYLE,
                properties: {
                    id: LIGHTBOX_IMAGE_ID
                }
            });
        }

        return {
            show(src, alt) {
                if (!src) {
                    return;
                }

                ensureElements();
                previouslyFocusedElement = document.activeElement instanceof HTMLElement
                    ? document.activeElement
                    : null;

                image.src = src;
                image.alt = alt ?? '';
                overlay.setAttribute('aria-hidden', 'false');

                if (!overlay.isConnected) {
                    previousBodyOverflow = document.body.style.overflow;
                    document.body.append(overlay);
                    document.addEventListener('keydown', handleDocumentKeyDown);
                    document.body.style.overflow = 'hidden';
                }

                (closeButton ?? overlay).focus();
            },
            hide
        };
    }

    function getLightboxController() {
        lightboxController ??= createLightboxController();
        return lightboxController;
    }

    window.cockpit.setupImagePaste = function (inputId, dotnetRef) {
        const element = getElement(inputId);
        if (!element) {
            return;
        }

        attachPasteHandler(element, dotnetRef);
    };

    window.cockpit.cleanupImagePaste = function (inputId) {
        detachPasteHandler(getElement(inputId));
    };

    window.cockpit.showImageLightbox = function (src, alt) {
        getLightboxController().show(src, alt);
    };
})();
