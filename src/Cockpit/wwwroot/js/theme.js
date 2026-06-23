/*
 * Theme DOM interop for Cockpit.
 * Keeps the small set of global hooks used by Blazor and the MAUI title bar.
 */
(() => {
    const cockpit = window.cockpit ??= {};
    const rootStyle = document.documentElement.style;
    const bodyClassList = document.body.classList;
    const toggleSettingsMethodName = 'ToggleSettingsFromTitleBar';

    let mainLayoutRef = null;

    function setRootProperty(propertyName, value) {
        if (!propertyName) {
            return;
        }

        const normalizedValue = value ?? '';
        if (rootStyle.getPropertyValue(propertyName) === normalizedValue) {
            return;
        }

        rootStyle.setProperty(propertyName, normalizedValue);
    }

    function setAccentColor(accentColor, accentHoverColor) {
        setRootProperty('--accent-color', accentColor);
        setRootProperty('--button-bg', accentColor);
        setRootProperty('--button-hover', accentHoverColor);
    }

    function setBodyClass(className, shouldApply) {
        if (!className) {
            return;
        }

        if (shouldApply) {
            bodyClassList.add(className);
            return;
        }

        bodyClassList.remove(className);
    }

    function toggleSettingsFromTitleBar() {
        const layoutRef = mainLayoutRef;
        if (!layoutRef) {
            return;
        }

        layoutRef.invokeMethodAsync(toggleSettingsMethodName)
            .catch((error) => console.error('Failed to toggle settings:', error));
    }

    Object.assign(cockpit, {
        setMainLayoutRef(dotNetRef) {
            mainLayoutRef = dotNetRef ?? null;
        },
        setRootProperty,
        setAccentColor,
        addBodyClass(className) {
            setBodyClass(className, true);
        },
        removeBodyClass(className) {
            setBodyClass(className, false);
        }
    });

    window.toggleSettings = toggleSettingsFromTitleBar;
})();
