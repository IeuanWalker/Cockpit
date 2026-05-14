/**
 * Dialog interop used by PopupBase.
 *
 * The current implementation intentionally stays in JavaScript because Blazor
 * can pass an ElementReference to JS, but it cannot invoke HTMLDialogElement
 * methods such as showModal() and close() directly from C#.
 *
 * Low-risk C# migration plan:
 * 1. Replace the native <dialog> in PopupBase with Blazor-managed markup plus
 *    CSS visibility toggling.
 * 2. Recreate native dialog behavior in C# and CSS, including backdrop,
 *    keyboard dismissal, focus management, and focus restoration.
 * 3. Update PopupBase callers to rely on component state instead of native
 *    dialog methods.
 * 4. Validate all popup flows that currently depend on the browser's dialog
 *    semantics before removing this interop.
 */
const cockpit = window.cockpit = window.cockpit || {};

/**
 * @param {unknown} element Candidate dialog element supplied by Blazor.
 * @returns {element is HTMLDialogElement}
 */
function isDialogElement(element) {
    return element instanceof HTMLDialogElement;
}

function isExpectedDialogStateError(error) {
    return error instanceof DOMException && error.name === 'InvalidStateError';
}

/**
 * Opens a native dialog when the supplied element is a closed <dialog>.
 * @param {HTMLDialogElement | null | undefined} element Dialog element supplied by Blazor.
 */
cockpit.openDialog = function (element) {
    if (!isDialogElement(element) || element.open || !element.isConnected) {
        return;
    }

    try {
        element.showModal();
    } catch (error) {
        if (!isExpectedDialogStateError(error)) {
            throw error;
        }
    }
};

/**
 * Closes a native dialog when the supplied element is an open <dialog>.
 * @param {HTMLDialogElement | null | undefined} element Dialog element supplied by Blazor.
 */
cockpit.closeDialog = function (element) {
    if (!isDialogElement(element) || !element.open) {
        return;
    }

    element.close();
};
