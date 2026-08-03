window.cockpit ??= {};

const cockpit = window.cockpit;

const logViewerStateByElement = new WeakMap();
const smartScrollStateByElement = new WeakMap();
const scrollAnchorStateByElement = new WeakMap();
const messageWindowStateByElement = new WeakMap();
const trackedScrollElements = new Set();
let detachedScrollElementObserver = null;

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
 * @property {boolean | undefined} lastReportedNearBottom
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

function resolveElement(elementOrId) {
    return typeof elementOrId === 'string'
        ? getElementById(elementOrId)
        : elementOrId;
}

function elementHasScrollState(element) {
    return logViewerStateByElement.has(element) ||
        smartScrollStateByElement.has(element) ||
        scrollAnchorStateByElement.has(element) ||
        messageWindowStateByElement.has(element);
}

function stopTrackingElementIfUnused(element) {
    if (elementHasScrollState(element)) {
        return;
    }

    trackedScrollElements.delete(element);
    if (trackedScrollElements.size === 0) {
        detachedScrollElementObserver?.disconnect();
        detachedScrollElementObserver = null;
    }
}

function disposeDetachedScrollElement(element) {
    disposeLogViewerState(element);
    disposeSmartScrollState(element);
    disposeScrollAnchorState(element);
    disposeMessageWindowState(element);
    trackedScrollElements.delete(element);
}

function trackScrollElement(element) {
    trackedScrollElements.add(element);
    if (detachedScrollElementObserver || !document.documentElement) {
        return;
    }

    detachedScrollElementObserver = new MutationObserver(() => {
        for (const trackedElement of Array.from(trackedScrollElements)) {
            if (!trackedElement.isConnected) {
                disposeDetachedScrollElement(trackedElement);
            }
        }
    });
    detachedScrollElementObserver.observe(document.documentElement, { childList: true, subtree: true });
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
    stopTrackingElementIfUnused(element);
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
    for (const subscriber of state.subscribers.values()) {
        subscriber.dotNetRef = null;
    }
    state.subscribers.clear();
    smartScrollStateByElement.delete(element);
    stopTrackingElementIfUnused(element);
}

function disposeScrollAnchorState(element) {
    const state = scrollAnchorStateByElement.get(element);
    if (!state) {
        return;
    }

    state.resizeObserver.disconnect();
    scrollAnchorStateByElement.delete(element);
    stopTrackingElementIfUnused(element);
}

function captureMessageWindowAnchor(element) {
    const containerRect = element.getBoundingClientRect();
    const items = element.querySelectorAll('[data-message-id]');

    for (const item of items) {
        const rect = item.getBoundingClientRect();
        if (rect.bottom > containerRect.top + 1) {
            return {
                messageId: item.dataset.messageId,
                offset: rect.top - containerRect.top
            };
        }
    }

    return null;
}

function correctMessageWindowAnchor(element, state, anchor) {
    if (!anchor?.messageId) {
        return false;
    }

    const escapedId = window.CSS?.escape
        ? window.CSS.escape(anchor.messageId)
        : anchor.messageId.replace(/["\\]/g, '\\$&');
    const item = element.querySelector(`[data-message-id="${escapedId}"]`);
    if (!item) {
        return false;
    }

    const containerRect = element.getBoundingClientRect();
    const actualOffset = item.getBoundingClientRect().top - containerRect.top;
    state.lastAnchorCorrectionAt = window.performance.now();
    element.scrollTop += actualOffset - anchor.offset;
    return true;
}

function stopMessageWindowAnchorCorrection(state) {
    state.anchorResizeObserver?.disconnect();
    state.anchorResizeObserver = null;
    clearWindowTimeout(state.anchorCorrectionTimerId);
    state.anchorCorrectionTimerId = null;
}

function startMessageWindowAnchorCorrection(element, state, anchor) {
    stopMessageWindowAnchorCorrection(state);
    if (!correctMessageWindowAnchor(element, state, anchor)) {
        return;
    }

    // Images, highlighted code, and expanded activity can settle after the render.
    // Keep the same visible message pinned during that short layout window.
    state.anchorResizeObserver = new ResizeObserver(() => {
        correctMessageWindowAnchor(element, state, anchor);
    });
    for (const item of element.querySelectorAll('[data-message-id]')) {
        state.anchorResizeObserver.observe(item);
    }
    state.anchorCorrectionTimerId = window.setTimeout(() => {
        stopMessageWindowAnchorCorrection(state);
    }, 1500);
}

function refreshMessageWindowTargets(element, state) {
    const topTarget = element.querySelector('[data-message-window-direction="older"]');
    const bottomTarget = element.querySelector('[data-message-window-direction="newer"]');

    if (state.topTarget !== topTarget) {
        if (state.topTarget) {
            state.intersectionObserver.unobserve(state.topTarget);
        }
        state.topTarget = topTarget;
        if (topTarget) {
            state.intersectionObserver.observe(topTarget);
        }
    }

    if (state.bottomTarget !== bottomTarget) {
        if (state.bottomTarget) {
            state.intersectionObserver.unobserve(state.bottomTarget);
        }
        state.bottomTarget = bottomTarget;
        if (bottomTarget) {
            state.intersectionObserver.observe(bottomTarget);
        }
    }
}

function disposeMessageWindowState(element) {
    const state = messageWindowStateByElement.get(element);
    if (!state) {
        return;
    }

    state.intersectionObserver.disconnect();
    state.mutationObserver.disconnect();
    stopMessageWindowAnchorCorrection(state);
    element.removeEventListener('wheel', state.handleAnchorInteraction, passiveScrollListenerOptions);
    element.removeEventListener('touchstart', state.handleAnchorInteraction, passiveScrollListenerOptions);
    element.removeEventListener('pointerdown', state.handleAnchorInteraction, passiveScrollListenerOptions);
    element.removeEventListener('scroll', state.handleAnchorScroll, passiveScrollListenerOptions);
    state.dotNetRef = null;
    state.topTarget = null;
    state.bottomTarget = null;
    messageWindowStateByElement.delete(element);
    stopTrackingElementIfUnused(element);
}

function getSmartScrollSubscriberId(subscriptionKey, methodName) {
    return subscriptionKey ?? methodName;
}

function notifySmartScrollSubscribers(state, nearBottom) {
    for (const subscriber of state.subscribers.values()) {
        // Track the last-reported value so that re-registrations (e.g. new
        // DotNetObjectReference on re-render) don't fire a redundant callback.
        subscriber.lastReportedNearBottom = nearBottom;
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

function canSmartScrollFollowTail(element) {
    const windowState = messageWindowStateByElement.get(element);
    return !windowState || (windowState.includesTail && !windowState.inFlight);
}

function suspendSmartScrollForMessageWindow(element) {
    const smartScrollState = smartScrollStateByElement.get(element);
    if (smartScrollState) {
        publishSmartScrollState(smartScrollState, false);
    }
}

function reconcileSmartScrollState(element, state, fromUserScroll) {
    if (!canSmartScrollFollowTail(element)) {
        publishSmartScrollState(state, false);
        return;
    }

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

    if (state.nearBottom && !state.recentInteraction && canSmartScrollFollowTail(element)) {
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

    const lastReportedNearBottom = existingSubscriber?.lastReportedNearBottom;
    const subscriber = { dotNetRef, methodName, lastReportedNearBottom };
    state.subscribers.set(subscriberId, subscriber);

    if (lastReportedNearBottom !== state.nearBottom) {
        subscriber.lastReportedNearBottom = state.nearBottom;
        invokeDotNetSafely(dotNetRef, methodName, state.nearBottom);
    }
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

cockpit.scrollToBottom = function scrollToBottom(elementOrId) {
    scrollElementToBottom(resolveElement(elementOrId));
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
    trackScrollElement(element);
};

cockpit.cleanupLogViewerScroll = function cleanupLogViewerScroll(elementId) {
    const element = getElementById(elementId);
    if (!element) {
        return;
    }

    disposeLogViewerState(element);
};

cockpit.setupSmartScroll = function setupSmartScroll(elementOrId, dotNetRef, methodName, subscriptionKey) {
    const element = resolveElement(elementOrId);
    if (!element) {
        return false;
    }

    const state = smartScrollStateByElement.get(element) ?? createSmartScrollState(element);
    upsertSmartScrollSubscriber(state, subscriptionKey, dotNetRef, methodName);
    trackScrollElement(element);
    return true;
};

cockpit.cleanupSmartScroll = function cleanupSmartScroll(elementOrId, subscriptionKey) {
    const element = resolveElement(elementOrId);
    if (!element) {
        return;
    }

    disposeSmartScrollSubscriber(element, subscriptionKey);
};

cockpit.scrollIntoView = function scrollIntoView(elementId) {
    document.getElementById(elementId)?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
};

cockpit.setupScrollAnchor = function setupScrollAnchor(elementOrId) {
    const element = resolveElement(elementOrId);
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
    trackScrollElement(element);
};

cockpit.cleanupScrollAnchor = function cleanupScrollAnchor(elementOrId) {
    const element = resolveElement(elementOrId);
    if (!element) {
        return;
    }

    disposeScrollAnchorState(element);
};

cockpit.setupMessageWindow = function setupMessageWindow(
    elementOrId,
    dotNetRef,
    methodName,
    includesTail,
    generation) {
    const element = resolveElement(elementOrId);
    if (!element || !dotNetRef) {
        return false;
    }

    disposeMessageWindowState(element);

    const state = {
        dotNetRef,
        methodName,
        generation,
        includesTail: includesTail === true,
        topTarget: null,
        bottomTarget: null,
        inFlight: false,
        anchorResizeObserver: null,
        anchorCorrectionTimerId: null,
        lastAnchorCorrectionAt: 0,
        handleAnchorInteraction: null,
        handleAnchorScroll: null,
        intersectionObserver: null,
        mutationObserver: null
    };

    state.intersectionObserver = new IntersectionObserver((entries) => {
        if (state.inFlight) {
            return;
        }

        const entry = entries.find(candidate => candidate.isIntersecting);
        const direction = entry?.target?.dataset?.messageWindowDirection;
        if (!direction) {
            return;
        }

        state.inFlight = true;
        suspendSmartScrollForMessageWindow(element);
        const anchor = captureMessageWindowAnchor(element);
        const callbackGeneration = state.generation;
        state.dotNetRef?.invokeMethodAsync(
            state.methodName,
            direction,
            anchor,
            callbackGeneration).catch(() => {
            if (state.generation === callbackGeneration) {
                state.inFlight = false;
            }
        });
    }, {
        root: element,
        rootMargin: '800px 0px 800px 0px',
        threshold: 0
    });

    state.mutationObserver = new MutationObserver(() => {
        refreshMessageWindowTargets(element, state);
    });
    state.mutationObserver.observe(element, { childList: true });

    state.handleAnchorInteraction = event => {
        if (event.isTrusted) {
            stopMessageWindowAnchorCorrection(state);
        }
    };
    state.handleAnchorScroll = event => {
        // Ignore the scroll event caused by the anchor correction itself. Wheel,
        // touch, and pointer input cancel immediately; trusted keyboard/scrollbar
        // scrolling cancels once the correction's own event has settled.
        if (event.isTrusted && window.performance.now() - state.lastAnchorCorrectionAt > 100) {
            stopMessageWindowAnchorCorrection(state);
        }
    };
    element.addEventListener('wheel', state.handleAnchorInteraction, passiveScrollListenerOptions);
    element.addEventListener('touchstart', state.handleAnchorInteraction, passiveScrollListenerOptions);
    element.addEventListener('pointerdown', state.handleAnchorInteraction, passiveScrollListenerOptions);
    element.addEventListener('scroll', state.handleAnchorScroll, passiveScrollListenerOptions);

    messageWindowStateByElement.set(element, state);
    trackScrollElement(element);
    refreshMessageWindowTargets(element, state);
    if (!state.includesTail) {
        suspendSmartScrollForMessageWindow(element);
    }
    return true;
};

cockpit.beginMessageWindowShift = function beginMessageWindowShift(elementOrId, generation) {
    const element = resolveElement(elementOrId);
    const state = element ? messageWindowStateByElement.get(element) : null;
    if (!element || !state || state.generation !== generation) {
        return false;
    }

    state.inFlight = true;
    suspendSmartScrollForMessageWindow(element);
    return true;
};

cockpit.setMessageWindowTailState = function setMessageWindowTailState(elementOrId, includesTail, generation) {
    const element = resolveElement(elementOrId);
    const state = element ? messageWindowStateByElement.get(element) : null;
    if (!element || !state || state.generation !== generation) {
        return false;
    }

    state.includesTail = includesTail === true;
    if (!state.includesTail) {
        suspendSmartScrollForMessageWindow(element);
    }
    return true;
};

cockpit.captureMessageWindowAnchor = function captureKnownMessageWindowAnchor(elementOrId) {
    const element = resolveElement(elementOrId);
    return element ? captureMessageWindowAnchor(element) : null;
};

cockpit.restoreMessageWindowAnchor = function restoreMessageWindowAnchor(elementOrId, anchor, generation) {
    const element = resolveElement(elementOrId);
    const state = element ? messageWindowStateByElement.get(element) : null;
    if (!element || !state || state.generation !== generation || !anchor) {
        return false;
    }

    startMessageWindowAnchorCorrection(element, state, anchor);
    refreshMessageWindowTargets(element, state);
    return true;
};

cockpit.completeMessageWindowShift = function completeMessageWindowShift(elementOrId, includesTail, generation) {
    const element = resolveElement(elementOrId);
    const state = element ? messageWindowStateByElement.get(element) : null;
    if (!state || state.generation !== generation) {
        return false;
    }

    state.includesTail = includesTail === true;
    state.inFlight = false;
    if (!state.includesTail) {
        suspendSmartScrollForMessageWindow(element);
    } else {
        const smartScrollState = smartScrollStateByElement.get(element);
        if (smartScrollState) {
            reconcileSmartScrollState(element, smartScrollState, true);
        }
    }
    refreshMessageWindowTargets(element, state);
    // Re-observing allows background prefetch to continue while the replacement
    // sentinel remains inside the generous root margin.
    for (const target of [state.topTarget, state.bottomTarget]) {
        if (target) {
            state.intersectionObserver.unobserve(target);
            state.intersectionObserver.observe(target);
        }
    }
    return true;
};

cockpit.cleanupMessageWindow = function cleanupMessageWindow(elementOrId) {
    const element = resolveElement(elementOrId);
    if (element) {
        disposeMessageWindowState(element);
    }
};
