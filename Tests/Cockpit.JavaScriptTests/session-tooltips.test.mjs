import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';

let nextTimerId = 1;
const timers = new Map();
const timerDelays = new Map();

class FakeStyle {
    removeProperty(name) { delete this[name]; }
}

class FakeElement {
    constructor() {
        this.isConnected = true;
        this.isHovered = false;
        this.isOpen = false;
        this.listeners = new Map();
        this.style = new FakeStyle();
    }

    addEventListener(name, callback) {
        this.listeners.set(name, callback);
    }

    contains(element) { return element === this; }
    getBoundingClientRect() { return { left: 10, right: 110, top: 20, width: 100, height: 40 }; }
    hidePopover() { this.isOpen = false; }
    matches(selector) {
        if (selector === ':hover') return this.isHovered;
        if (selector === ':popover-open') return this.isOpen;
        return false;
    }
    showPopover() { this.isOpen = true; }
}

globalThis.HTMLElement = FakeElement;
globalThis.document = {
    activeElement: null,
    getElementById: () => null
};
globalThis.window = {
    cockpit: {},
    innerHeight: 800,
    innerWidth: 1200,
    setTimeout: (callback, delay) => {
        const id = nextTimerId++;
        timers.set(id, callback);
        timerDelays.set(id, delay);
        return id;
    },
    clearTimeout: id => {
        timers.delete(id);
        timerDelays.delete(id);
    },
    requestAnimationFrame: () => 1,
    cancelAnimationFrame: () => {}
};

const source = fs.readFileSync(
    new URL('../../src/Cockpit/wwwroot/js/dom-utils.js', import.meta.url),
    'utf8');
vm.runInThisContext(source, { filename: 'dom-utils.js' });

function runPendingTimers() {
    const callbacks = [...timers.values()];
    timers.clear();
    timerDelays.clear();
    callbacks.forEach(callback => callback());
}

test('session tooltips wait for hover intent and only open the latest card', () => {
    const firstAnchor = new FakeElement();
    const firstTooltip = new FakeElement();
    const secondAnchor = new FakeElement();
    const secondTooltip = new FakeElement();

    firstAnchor.isHovered = true;
    window.cockpit.showSessionTooltip(firstAnchor, firstTooltip);
    assert.equal(firstTooltip.isOpen, false);
    assert.deepEqual([...timerDelays.values()], [400]);

    firstAnchor.isHovered = false;
    window.cockpit.hideSessionTooltip(firstTooltip);
    secondAnchor.isHovered = true;
    window.cockpit.showSessionTooltip(secondAnchor, secondTooltip);
    runPendingTimers();

    assert.equal(firstTooltip.isOpen, false);
    assert.equal(secondTooltip.isOpen, true);

    firstAnchor.isHovered = true;
    window.cockpit.showSessionTooltip(firstAnchor, firstTooltip);

    assert.equal(secondTooltip.isOpen, false, 'the current card closes before the next delay');
    assert.equal(firstTooltip.isOpen, false);

    runPendingTimers();
    assert.equal(firstTooltip.isOpen, true);
});

test('leaving before the intent delay cancels the pending card', () => {
    const anchor = new FakeElement();
    const tooltip = new FakeElement();

    anchor.isHovered = true;
    window.cockpit.showSessionTooltip(anchor, tooltip);
    anchor.isHovered = false;
    window.cockpit.hideSessionTooltip(tooltip);
    runPendingTimers();

    assert.equal(tooltip.isOpen, false);
});
