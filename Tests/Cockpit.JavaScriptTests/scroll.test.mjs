import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';

const animationFrames = [];

class FakeObserver {
    static instances = [];

    constructor(callback) {
        this.callback = callback;
        this.observed = new Set();
        this.constructor.instances.push(this);
    }

    observe(target) { this.observed.add(target); }
    unobserve(target) { this.observed.delete(target); }
    disconnect() { this.observed.clear(); }
}

class FakeResizeObserver extends FakeObserver { static instances = []; }
class FakeMutationObserver extends FakeObserver { static instances = []; }
class FakeIntersectionObserver extends FakeObserver {
    static instances = [];

    constructor(callback, options) {
        super(callback);
        this.options = options;
    }
}

class FakeElement {
    constructor() {
        this.scrollHeight = 1000;
        this.scrollTop = 500;
        this.clientHeight = 500;
        this.isConnected = true;
        this.listeners = new Map();
        this.topSentinel = { dataset: { messageWindowDirection: 'older' } };
        this.bottomSentinel = { dataset: { messageWindowDirection: 'newer' } };
        this.children = [this.topSentinel, this.bottomSentinel];
        this.messageItems = [];
        this.messageSelectorTargets = new Map();
        this.queriedSelectors = [];
    }

    addEventListener(name, callback) {
        const callbacks = this.listeners.get(name) ?? [];
        callbacks.push(callback);
        this.listeners.set(name, callbacks);
    }

    removeEventListener(name, callback) {
        this.listeners.set(name, (this.listeners.get(name) ?? []).filter(item => item !== callback));
    }

    dispatch(name, event = { isTrusted: true }) {
        for (const callback of this.listeners.get(name) ?? []) {
            callback(event);
        }
    }

    querySelector(selector) {
        this.queriedSelectors.push(selector);
        if (selector === '[data-message-window-direction="older"]') return this.topSentinel;
        if (selector === '[data-message-window-direction="newer"]') return this.bottomSentinel;
        if (this.messageSelectorTargets.has(selector)) return this.messageSelectorTargets.get(selector);
        return null;
    }

    querySelectorAll(selector) {
        return selector === '[data-message-id]' ? this.messageItems : [];
    }
    getBoundingClientRect() { return { top: 0, bottom: this.clientHeight }; }
}

function createMessageItem(messageId, top, bottom) {
    return {
        dataset: { messageId },
        getBoundingClientRect: () => ({ top, bottom })
    };
}

const documentElement = new FakeElement();
globalThis.document = {
    documentElement,
    getElementById: () => null
};
globalThis.ResizeObserver = FakeResizeObserver;
globalThis.MutationObserver = FakeMutationObserver;
globalThis.IntersectionObserver = FakeIntersectionObserver;
globalThis.window = {
    cockpit: {},
    CSS: { escape: value => value },
    performance: { now: () => 1000 },
    setTimeout,
    clearTimeout,
    requestAnimationFrame: callback => {
        animationFrames.push(callback);
        return animationFrames.length;
    },
    cancelAnimationFrame: () => {}
};

const scrollSource = fs.readFileSync(
    new URL('../../src/Cockpit/wwwroot/js/scroll.js', import.meta.url),
    'utf8');
vm.runInThisContext(scrollSource, { filename: 'scroll.js' });

function flushAnimationFrames() {
    while (animationFrames.length > 0) {
        animationFrames.shift()();
    }
}

function createHarness(includesTail, generation = 1) {
    const element = new FakeElement();
    const notifications = [];
    const boundaries = [];
    const smartRef = {
        invokeMethodAsync: (_method, value) => {
            notifications.push(value);
            return Promise.resolve();
        }
    };
    const windowRef = {
        invokeMethodAsync: (_method, direction, anchor, callbackGeneration) => {
            boundaries.push({ direction, anchor, generation: callbackGeneration });
            return Promise.resolve();
        }
    };

    window.cockpit.setupSmartScroll(element, smartRef, 'OnScroll', 'test');
    const smartResizeObserver = FakeResizeObserver.instances.at(-1);
    const smartMutationObserver = FakeMutationObserver.instances.at(-1);
    window.cockpit.setupMessageWindow(element, windowRef, 'OnBoundary', includesTail, generation);
    const intersectionObserver = FakeIntersectionObserver.instances.at(-1);

    return {
        element,
        notifications,
        boundaries,
        smartResizeObserver,
        smartMutationObserver,
        intersectionObserver
    };
}

function cleanup(element) {
    window.cockpit.cleanupMessageWindow(element);
    window.cockpit.cleanupSmartScroll(element, 'test');
}

test('observed DOM changes do not auto-follow a rendered window that excludes the conversation tail', () => {
    const harness = createHarness(false);
    harness.element.scrollHeight = 1400;

    harness.smartResizeObserver.callback();
    flushAnimationFrames();

    assert.equal(harness.element.scrollTop, 500);
    assert.equal(harness.notifications.at(-1), false);
    cleanup(harness.element);
});

test('bottom-sentinel MoveNewer shifts cannot force-scroll through non-tail history', () => {
    const harness = createHarness(false);
    harness.intersectionObserver.callback([
        { isIntersecting: true, target: harness.element.bottomSentinel }
    ]);
    harness.element.scrollHeight = 1600;

    harness.smartResizeObserver.callback();
    flushAnimationFrames();
    window.cockpit.completeMessageWindowShift(harness.element, false, 1);
    harness.smartResizeObserver.callback();
    flushAnimationFrames();

    assert.equal(harness.boundaries.length, 1);
    assert.equal(harness.boundaries[0].direction, 'newer');
    assert.equal(harness.boundaries[0].generation, 1);
    assert.equal(harness.element.scrollTop, 500);
    cleanup(harness.element);
});

test('auto-follow resumes only after the true tail is rendered and reached', () => {
    const harness = createHarness(false);
    harness.element.scrollHeight = 1400;
    window.cockpit.beginMessageWindowShift(harness.element, 1);
    window.cockpit.completeMessageWindowShift(harness.element, true, 1);

    assert.equal(harness.element.scrollTop, 500);
    harness.element.scrollTop = 900;
    harness.element.dispatch('scroll');
    harness.element.scrollHeight = 1600;
    harness.smartResizeObserver.callback();
    flushAnimationFrames();

    assert.equal(harness.notifications.at(-1), true);
    assert.equal(harness.element.scrollTop, 1600);
    cleanup(harness.element);
});

test('stale generation completions cannot alter a re-registered message window', () => {
    const harness = createHarness(false, 7);

    assert.equal(window.cockpit.beginMessageWindowShift(harness.element, 6), false);
    assert.equal(window.cockpit.setMessageWindowTailState(harness.element, true, 6), false);
    assert.equal(window.cockpit.completeMessageWindowShift(harness.element, true, 6), false);

    harness.element.scrollHeight = 1400;
    harness.smartResizeObserver.callback();
    flushAnimationFrames();

    assert.equal(harness.element.scrollTop, 500);
    assert.equal(harness.notifications.at(-1), false);
    cleanup(harness.element);
});

test('remote append tail-state sync prevents observer auto-follow after user browses up', () => {
    const harness = createHarness(true, 9);

    // The user leaves the tail while the rendered window still contains it.
    harness.element.scrollTop = 400;
    harness.element.dispatch('scroll');
    assert.equal(harness.notifications.at(-1), false);

    // A remote append freezes the C# window at its old end. Publish that new
    // relationship before the mutation/resize animation frame reconciles it.
    assert.equal(window.cockpit.setMessageWindowTailState(harness.element, false, 9), true);
    harness.element.scrollHeight = 950;
    harness.smartMutationObserver.callback([
        { type: 'childList', target: harness.element }
    ]);
    flushAnimationFrames();

    // A later resize would force-follow if the first observer pass had been
    // allowed to restore smart-scroll's near-bottom state while tail was stale.
    harness.element.scrollHeight = 1200;
    harness.smartResizeObserver.callback();
    flushAnimationFrames();

    assert.equal(harness.element.scrollTop, 400);
    assert.equal(harness.notifications.at(-1), false);
    cleanup(harness.element);
});

test('boundary callback carries the observer registration generation', () => {
    const harness = createHarness(false, 42);

    harness.intersectionObserver.callback([
        { isIntersecting: true, target: harness.element.topSentinel }
    ]);

    assert.equal(harness.boundaries.length, 1);
    assert.equal(harness.boundaries[0].direction, 'older');
    assert.equal(harness.boundaries[0].generation, 42);
    cleanup(harness.element);
});

test('message-window anchor capture selects the first visible message and records its offset', () => {
    const element = new FakeElement();
    element.getBoundingClientRect = () => ({ top: 100, bottom: 600 });
    element.messageItems = [
        createMessageItem('above', 50, 100),
        createMessageItem('edge', 75, 101),
        createMessageItem('visible', 90, 102)
    ];

    assert.deepEqual(window.cockpit.captureMessageWindowAnchor(element), {
        messageId: 'visible',
        offset: -10
    });
});

test('message-window anchor correction escapes the id and follows later layout changes', () => {
    const harness = createHarness(false, 12);
    const message = createMessageItem('message:42', 140, 180);
    const selector = '[data-message-id="escaped-message-id"]';
    harness.element.messageItems = [message];
    harness.element.messageSelectorTargets.set(selector, message);
    const originalEscape = window.CSS.escape;
    window.CSS.escape = value => {
        assert.equal(value, 'message:42');
        return 'escaped-message-id';
    };

    try {
        assert.equal(window.cockpit.restoreMessageWindowAnchor(
            harness.element,
            { messageId: 'message:42', offset: 100 },
            12), true);
        assert.equal(harness.element.scrollTop, 540);
        assert.equal(harness.element.queriedSelectors.includes(selector), true);

        const anchorResizeObserver = FakeResizeObserver.instances.at(-1);
        assert.equal(anchorResizeObserver.observed.has(message), true);
        message.getBoundingClientRect = () => ({ top: 160, bottom: 200 });
        anchorResizeObserver.callback();
        assert.equal(harness.element.scrollTop, 600);
    } finally {
        window.CSS.escape = originalEscape;
        cleanup(harness.element);
    }
});

test('message-window anchor correction falls back when CSS.escape is unavailable', () => {
    const harness = createHarness(false, 13);
    const messageId = 'message"with\\slashes';
    const message = createMessageItem(messageId, 25, 50);
    const selector = '[data-message-id="message\\"with\\\\slashes"]';
    harness.element.messageItems = [message];
    harness.element.messageSelectorTargets.set(selector, message);
    const originalCss = window.CSS;
    window.CSS = undefined;

    try {
        assert.equal(window.cockpit.restoreMessageWindowAnchor(
            harness.element,
            { messageId, offset: 10 },
            13), true);
        assert.equal(harness.element.scrollTop, 515);
        assert.equal(harness.element.queriedSelectors.includes(selector), true);
    } finally {
        window.CSS = originalCss;
        cleanup(harness.element);
    }
});

test('detached tracked elements are fully disposed by the document observer', () => {
    const harness = createHarness(false);
    const documentObserver = FakeMutationObserver.instances.find(
        observer => observer.observed.has(documentElement));
    const messageMutationObserver = FakeMutationObserver.instances.at(-1);

    assert.ok(documentObserver);
    harness.element.isConnected = false;
    documentObserver.callback([{ type: 'childList', target: documentElement }]);

    assert.equal(harness.smartResizeObserver.observed.size, 0);
    assert.equal(harness.smartMutationObserver.observed.size, 0);
    assert.equal(messageMutationObserver.observed.size, 0);
    assert.equal(harness.intersectionObserver.observed.size, 0);
    assert.equal(documentObserver.observed.size, 0);
    for (const eventName of ['scroll', 'click', 'wheel', 'touchstart', 'pointerdown']) {
        assert.equal(harness.element.listeners.get(eventName)?.length ?? 0, 0, eventName);
    }
});
