const cockpit = window.cockpit = window.cockpit || {};

const logViewerStateByElement = new WeakMap();
const smartScrollStateByElement = new WeakMap();
const scrollAnchorStateByElement = new WeakMap();

const passiveScrollListenerOptions = { passive: true };
const captureClickListenerOptions = { capture: true };

const logViewerNearBottomThresholdPx = 80;
const smartScrollNearBottomThresholdPx = 50;
const recentInteractionWindowMs = 500;

/**
 * @typedef {object} LogViewerState
 * @property {boolean | null} lastReportedNearBottom
 * @property {() => void} handleScroll
 */

/**
 * @typedef {object} SmartScrollSubscriber
 * @property {any} dotNetRef
 * @property {string} methodName
 */

/**
 * @typedef {object} SmartScrollState
 * @property {boolean} nearBottom
 * @property {boolean} recentInteraction
 * @property {number | null} interactionResetTimerId
 * @property {number | null} pendingAnimationFrameId
 * @property {boolean} needsChildObservationRefresh
 * @property {Set<Element>} observedDirectChildren
 * @property {Map<string, SmartScrollSubscriber>} subscribers
 * @property {(() => void) | null} handleScroll
 * @property {(() => void) | null} handleClickCapture
 * @property {ResizeObserver | null} resizeObserver
 * @property {MutationObserver | null} mutationObserver
 */

/**
 * @typedef {object} ScrollAnchorState
 * @property {number} lastClientHeight
 * @property {ResizeObserver} resizeObserver
 */

function getElementById(elementId) {
    return document.getElementById(elementId);
}

function getDistanceFromBottom(element) {
    return element.scrollHeight - element.scrollTop - element.clientHeight;
}

function isElementNearBottom(element, thresholdPx) {
    return getDistanceFromBottom(element) < thresholdPx;
}

function scrollElementToBottom(element) {
    if (element) {
        element.scrollTop = element.scrollHeight;
    }
}

function invokeDotNetSafely(dotNetRef, methodName, value) {
    if (!dotNetRef || typeof dotNetRef.invokeMethodAsync !== 'function') {
        return;
    }

    dotNetRef.invokeMethodAsync(methodName, value).catch(() => {
        // Ignore failures when the .NET component has already been disposed.
    });
}

function clearWindowTimeout(timerId) {
    if (timerId !== null) {
        window.clearTimeout(timerId);
    }
}

function cancelWindowAnimationFrame(frameId) {
    if (frameId !== null) {
        window.cancelAnimationFrame(frameId);
    }
}

function disposeLogViewerState(element) {
    const state = logViewerStateByElement.get(element);
    if (!state) {
        return;
    }

    element.removeEventListener('scroll', state.handleScroll, passiveScrollListenerOptions);
    logViewerStateByElement.delete(element);
}

function disposeSmartScrollState(element) {
    const state = smartScrollStateByElement.get(element);
    if (!state) {
        return;
    }

    if (state.handleScroll) {
        element.removeEventListener('scroll', state.handleScroll, passiveScrollListenerOptions);
    }

    if (state.handleClickCapture) {
        element.removeEventListener('click', state.handleClickCapture, captureClickListenerOptions);
    }

    state.resizeObserver?.disconnect();
    state.mutationObserver?.disconnect();
    clearWindowTimeout(state.interactionResetTimerId);
    cancelWindowAnimationFrame(state.pendingAnimationFrameId);
    state.observedDirectChildren.clear();
    state.subscribers.clear();
    smartScrollStateByElement.delete(element);
}

function disposeScrollAnchorState(element) {
    const state = scrollAnchorStateByElement.get(element);
    if (!state) {
        return;
    }

    state.resizeObserver.disconnect();
    scrollAnchorStateByElement.delete(element);
}

function getSmartScrollSubscriberId(subscriptionKey, methodName) {
    return subscriptionKey ?? methodName;
}

function notifySmartScrollSubscribers(state, nearBottom) {
    for (const subscriber of state.subscribers.values()) {
        invokeDotNetSafely(subscriber.dotNetRef, subscriber.methodName, nearBottom);
    }
}

function publishSmartScrollState(state, nearBottom) {
    if (nearBottom === state.nearBottom) {
        return;
    }

    state.nearBottom = nearBottom;
    notifySmartScrollSubscribers(state, nearBottom);
}

function reconcileSmartScrollState(element, state, fromUserScroll) {
    const nearBottom = isElementNearBottom(element, smartScrollNearBottomThresholdPx);
    if (nearBottom === state.nearBottom) {
        return;
    }

    if (!nearBottom && !fromUserScroll) {
        if (state.recentInteraction) {
            publishSmartScrollState(state, false);
        } else {
            scrollElementToBottom(element);
        }
        return;
    }

    publishSmartScrollState(state, nearBottom);
}

function synchronizeObservedDirectChildren(element, state) {
    if (!state.needsChildObservationRefresh) {
        return;
    }

    const resizeObserver = state.resizeObserver;
    if (!resizeObserver) {
        return;
    }

    state.needsChildObservationRefresh = false;
    const currentDirectChildren = new Set(element.children);

    for (const observedChild of Array.from(state.observedDirectChildren)) {
        if (currentDirectChildren.has(observedChild)) {
            continue;
        }

        resizeObserver.unobserve(observedChild);
        state.observedDirectChildren.delete(observedChild);
    }

    for (const child of currentDirectChildren) {
        if (state.observedDirectChildren.has(child)) {
            continue;
        }

        state.observedDirectChildren.add(child);
        resizeObserver.observe(child);
    }
}

function processSmartScrollObservedChange(element, state) {
    synchronizeObservedDirectChildren(element, state);

    if (state.nearBottom && !state.recentInteraction) {
        scrollElementToBottom(element);
        return;
    }

    reconcileSmartScrollState(element, state, false);
}

function scheduleSmartScrollObservedChange(element, state) {
    if (state.pendingAnimationFrameId !== null) {
        return;
    }

    state.pendingAnimationFrameId = window.requestAnimationFrame(() => {
        state.pendingAnimationFrameId = null;
        processSmartScrollObservedChange(element, state);
    });
}

function markSmartScrollInteraction(state) {
    state.recentInteraction = true;
    clearWindowTimeout(state.interactionResetTimerId);
    state.interactionResetTimerId = window.setTimeout(() => {
        state.recentInteraction = false;
        state.interactionResetTimerId = null;
    }, recentInteractionWindowMs);
}

function createSmartScrollState(element) {
    /** @type {SmartScrollState} */
    const state = {
        nearBottom: isElementNearBottom(element, smartScrollNearBottomThresholdPx),
        recentInteraction: false,
        interactionResetTimerId: null,
        pendingAnimationFrameId: null,
        needsChildObservationRefresh: true,
        observedDirectChildren: new Set(),
        subscribers: new Map(),
        handleScroll: null,
        handleClickCapture: null,
        resizeObserver: null,
        mutationObserver: null
    };

    state.handleScroll = () => {
        reconcileSmartScrollState(element, state, true);
    };

    state.handleClickCapture = () => {
        markSmartScrollInteraction(state);
    };

    state.resizeObserver = new ResizeObserver(() => {
        scheduleSmartScrollObservedChange(element, state);
    });

    state.mutationObserver = new MutationObserver((records) => {
        if (records.some(record => record.type === 'childList' && record.target === element)) {
            state.needsChildObservationRefresh = true;
        }

        scheduleSmartScrollObservedChange(element, state);
    });

    state.resizeObserver.observe(element);
    synchronizeObservedDirectChildren(element, state);

    element.addEventListener('scroll', state.handleScroll, passiveScrollListenerOptions);
    element.addEventListener('click', state.handleClickCapture, captureClickListenerOptions);

    state.mutationObserver.observe(element, {
        childList: true,
        subtree: true,
        characterData: true
    });

    smartScrollStateByElement.set(element, state);
    return state;
}

function upsertSmartScrollSubscriber(state, subscriptionKey, dotNetRef, methodName) {
    const subscriberId = getSmartScrollSubscriberId(subscriptionKey, methodName);
    const existingSubscriber = state.subscribers.get(subscriberId);
    if (existingSubscriber?.dotNetRef === dotNetRef && existingSubscriber?.methodName === methodName) {
        return;
    }

    state.subscribers.set(subscriberId, { dotNetRef, methodName });
    invokeDotNetSafely(dotNetRef, methodName, state.nearBottom);
}

function disposeSmartScrollSubscriber(element, subscriptionKey) {
    const state = smartScrollStateByElement.get(element);
    if (!state) {
        return;
    }

    if (subscriptionKey === undefined || subscriptionKey === null) {
        disposeSmartScrollState(element);
        return;
    }

    state.subscribers.delete(subscriptionKey);
    if (state.subscribers.size === 0) {
        disposeSmartScrollState(element);
    }
}

cockpit.scrollToBottom = function scrollToBottomById(elementId) {
    scrollElementToBottom(getElementById(elementId));
};

cockpit.scrollElementToBottom = function scrollKnownElementToBottom(element) {
    scrollElementToBottom(element);
};

cockpit.setupLogViewerScroll = function setupLogViewerScroll(elementId, dotNetRef, methodName) {
    const element = getElementById(elementId);
    if (!element) {
        return;
    }

    disposeLogViewerState(element);

    /** @type {LogViewerState} */
    const state = {
        lastReportedNearBottom: null,
        handleScroll: () => {
            const nearBottom = isElementNearBottom(element, logViewerNearBottomThresholdPx);
            if (nearBottom === state.lastReportedNearBottom) {
                return;
            }

            state.lastReportedNearBottom = nearBottom;
            invokeDotNetSafely(dotNetRef, methodName, nearBottom);
        }
    };

    element.addEventListener('scroll', state.handleScroll, passiveScrollListenerOptions);
    logViewerStateByElement.set(element, state);
};

cockpit.cleanupLogViewerScroll = function cleanupLogViewerScroll(elementId) {
    const element = getElementById(elementId);
    if (!element) {
        return;
    }

    disposeLogViewerState(element);
};

cockpit.setupSmartScroll = function setupSmartScroll(elementId, dotNetRef, methodName, subscriptionKey) {
    const element = getElementById(elementId);
    if (!element) {
        return;
    }

    const state = smartScrollStateByElement.get(element) ?? createSmartScrollState(element);
    upsertSmartScrollSubscriber(state, subscriptionKey, dotNetRef, methodName);
};

cockpit.cleanupSmartScroll = function cleanupSmartScroll(elementId, subscriptionKey) {
    const element = getElementById(elementId);
    if (!element) {
        return;
    }

    disposeSmartScrollSubscriber(element, subscriptionKey);
};

cockpit.setupScrollAnchor = function setupScrollAnchor(elementId) {
    const element = getElementById(elementId);
    if (!element) {
        return;
    }

    disposeScrollAnchorState(element);

    /** @type {ScrollAnchorState} */
    const state = {
        lastClientHeight: element.clientHeight,
        resizeObserver: new ResizeObserver(() => {
            const nextClientHeight = element.clientHeight;
            const heightDelta = state.lastClientHeight - nextClientHeight;

            if (heightDelta > 0) {
                element.scrollTop += heightDelta;
            }

            state.lastClientHeight = nextClientHeight;
        })
    };

    state.resizeObserver.observe(element);
    scrollAnchorStateByElement.set(element, state);
};

cockpit.cleanupScrollAnchor = function cleanupScrollAnchor(elementId) {
    const element = getElementById(elementId);
    if (!element) {
        return;
    }

    disposeScrollAnchorState(element);
};
