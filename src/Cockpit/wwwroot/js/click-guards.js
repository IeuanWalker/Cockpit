(function () {
    if (window.__cockpitClickGuardsInitialized) {
        return;
    }

    window.__cockpitClickGuardsInitialized = true;

    const clickIsolationContainerSelectors = [
        '.event-message',
        '.tool-summary',
        '.thinking-message',
        '.working-message',
    ];
    const clickIsolationContainerSelector = clickIsolationContainerSelectors.join(', ');
    const clickIsolationInteractiveSelector = 'a, button';
    const doubleClickGuardSelector = '.event-message, .error-message';
    const doubleClickThresholdMs = 500;
    const doubleClickStateTtlMs = doubleClickThresholdMs * 2;
    const doubleClickStatePruneThreshold = 64;
    const captureListenerOptions = { capture: true };
    const mutationObserverOptions = { childList: true, subtree: true };

    const isolatedClickTargets = new WeakSet();
    const doubleClickStatesById = new Map();
    const doubleClickStatesByElement = new WeakMap();

    let nextDoubleClickPruneAt = 0;

    function asElement(node) {
        return node && node.nodeType === Node.ELEMENT_NODE ? node : null;
    }

    function getTargetElement(node) {
        return asElement(node) || node?.parentElement || null;
    }

    function getClosestMatch(node, selector) {
        const element = getTargetElement(node);
        return element?.closest?.(selector) || null;
    }

    function isInsideClickIsolationContainer(element) {
        return !!element.closest(clickIsolationContainerSelector);
    }

    function stopClickPropagation(event) {
        event.stopPropagation();
    }

    function isolateClickTarget(element) {
        if (!isInsideClickIsolationContainer(element) || isolatedClickTargets.has(element)) {
            return;
        }

        isolatedClickTargets.add(element);
        element.addEventListener('click', stopClickPropagation);
    }

    function scanClickIsolationTargets(root) {
        if (!root || typeof root.querySelectorAll !== 'function') {
            return;
        }

        const targets = root.querySelectorAll(clickIsolationInteractiveSelector);
        for (let index = 0; index < targets.length; index++) {
            isolateClickTarget(targets[index]);
        }
    }

    function shouldScanAddedIsolationElement(element) {
        if (element.childElementCount === 0) {
            return false;
        }

        if (!element.querySelector(clickIsolationInteractiveSelector)) {
            return false;
        }

        return isInsideClickIsolationContainer(element) || !!element.querySelector(clickIsolationContainerSelector);
    }

    function handleAddedIsolationElement(element) {
        if (element.matches(clickIsolationInteractiveSelector)) {
            isolateClickTarget(element);
        }

        if (shouldScanAddedIsolationElement(element)) {
            scanClickIsolationTargets(element);
        }
    }

    function startClickIsolation(root) {
        scanClickIsolationTargets(root);

        const observer = new MutationObserver((mutations) => {
            const addedElements = new Set();

            for (let mutationIndex = 0; mutationIndex < mutations.length; mutationIndex++) {
                const addedNodes = mutations[mutationIndex].addedNodes;
                for (let nodeIndex = 0; nodeIndex < addedNodes.length; nodeIndex++) {
                    const element = asElement(addedNodes[nodeIndex]);
                    if (element) {
                        addedElements.add(element);
                    }
                }
            }

            addedElements.forEach(handleAddedIsolationElement);
        });

        observer.observe(root, mutationObserverOptions);
    }

    function createDoubleClickState() {
        return { lastMouseDownAt: 0, suppressUntil: 0 };
    }

    function getDoubleClickStateId(element) {
        return element.dataset.cockpitId || null;
    }

    function getDoubleClickState(element, createIfMissing) {
        const stateId = getDoubleClickStateId(element);
        const stateStore = stateId ? doubleClickStatesById : doubleClickStatesByElement;
        const stateKey = stateId || element;
        let state = stateStore.get(stateKey);

        if (!state && createIfMissing) {
            state = createDoubleClickState();
            stateStore.set(stateKey, state);
        }

        return state || null;
    }

    function deleteDoubleClickState(element) {
        const stateId = getDoubleClickStateId(element);
        if (stateId) {
            doubleClickStatesById.delete(stateId);
            return;
        }

        doubleClickStatesByElement.delete(element);
    }

    function pruneDoubleClickStates(now) {
        if (doubleClickStatesById.size < doubleClickStatePruneThreshold || now < nextDoubleClickPruneAt) {
            return;
        }

        nextDoubleClickPruneAt = now + doubleClickStateTtlMs;

        doubleClickStatesById.forEach((state, key) => {
            if (state.suppressUntil <= now && now - state.lastMouseDownAt >= doubleClickStateTtlMs) {
                doubleClickStatesById.delete(key);
            }
        });
    }

    function onDoubleClickGuardMouseDown(event) {
        if (event.button !== 0) {
            return;
        }

        const target = getClosestMatch(event.target, doubleClickGuardSelector);
        if (!target) {
            return;
        }

        const now = Date.now();
        const state = getDoubleClickState(target, true);
        if (now - state.lastMouseDownAt < doubleClickThresholdMs) {
            state.suppressUntil = now + doubleClickThresholdMs;
        } else {
            state.suppressUntil = 0;
        }

        state.lastMouseDownAt = now;
        pruneDoubleClickStates(now);
    }

    function onDoubleClickGuardClick(event) {
        const target = getClosestMatch(event.target, doubleClickGuardSelector);
        if (!target) {
            return;
        }

        const now = Date.now();
        const state = getDoubleClickState(target, false);
        if (!state) {
            return;
        }

        if (event.detail >= 2 || now < state.suppressUntil) {
            event.stopPropagation();
            event.stopImmediatePropagation();
            deleteDoubleClickState(target);
            return;
        }

        if (now - state.lastMouseDownAt >= doubleClickThresholdMs) {
            deleteDoubleClickState(target);
        }
    }

    function startDoubleClickGuard() {
        window.addEventListener('mousedown', onDoubleClickGuardMouseDown, captureListenerOptions);
        window.addEventListener('click', onDoubleClickGuardClick, captureListenerOptions);
    }

    function start() {
        const root = document.body;
        if (!root) {
            return;
        }

        startClickIsolation(root);
        startDoubleClickGuard();
    }

    if (document.body) {
        start();
        return;
    }

    document.addEventListener('DOMContentLoaded', start, { once: true });
})();
