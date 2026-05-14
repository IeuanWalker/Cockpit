/*
 * Exposes a minimal JSInterop wrapper for browser localStorage access from C#.
 *
 * C# migration plan:
 * - There are currently no C# or Razor callers for window.localStorageHelper; only src/Cockpit/wwwroot/index.html loads this file.
 * - If storage moves to managed code, replace future JSInterop callers with UserAppSettings/IPreferencesStorage or a dedicated service registered in MauiProgram.cs.
 * - Likely files to update are MauiProgram.cs, UserAppSettings.cs or a new storage service, any future IJSRuntime callers, and wwwroot/index.html when the script is removed.
 * - Risks: browser localStorage and MAUI Preferences have different storage scopes and lifecycle behavior, so key names, null handling, and migration expectations must be aligned first.
 * - Additional complexity: src/Cockpit/wwwroot/js/interop.js contains a duplicate localStorageHelper definition, so both assets must be reconciled before removing JS.
 * - Recommended approach: confirm the intended persistence contract, move it behind a C# abstraction backed by IPreferencesStorage, add unit tests for key/default behavior, then remove the JS helper and script reference.
 */
(() => {
    if (window.localStorageHelper) {
        return;
    }

    let storage;

    function getStorage() {
        if (storage) {
            return storage;
        }

        if (!('localStorage' in window)) {
            throw new Error('localStorage is not available in this context.');
        }

        storage = window.localStorage;
        return storage;
    }

    function getItem(key) {
        return getStorage().getItem(key);
    }

    function setItem(key, value) {
        getStorage().setItem(key, value);
    }

    function removeItem(key) {
        getStorage().removeItem(key);
    }

    window.localStorageHelper = Object.freeze({
        getItem,
        setItem,
        removeItem
    });
})();
