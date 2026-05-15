(function () {
    window.cockpit ??= {};
    const cockpit = window.cockpit;

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
})();
