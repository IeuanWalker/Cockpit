import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';

const classList = (...initial) => {
    const values = new Set(initial);
    return {
        add: value => values.add(value),
        remove: value => values.delete(value),
        contains: value => values.has(value),
        toggle: (value, force) => force ? values.add(value) : values.delete(value)
    };
};

globalThis.requestAnimationFrame = callback => callback();
globalThis.getComputedStyle = () => ({ columnGap: '2px' });
globalThis.document = { fonts: { ready: Promise.resolve() } };
globalThis.Element = class {};
let lastResizeObserver;
globalThis.ResizeObserver = class {
    constructor(callback) { this.callback = callback; lastResizeObserver = this; }
    observe() {}
    disconnect() { this.disconnected = true; }
};

const moduleSource = fs.readFileSync(new URL('../../src/Cockpit/wwwroot/js/workspace-tabs.js', import.meta.url), 'utf8');
const tabsModule = await import(`data:text/javascript;base64,${Buffer.from(moduleSource).toString('base64')}`);

function fixture(widths, available, activeIndex = 0) {
    const tabs = widths.map((naturalWidth, index) => ({
        dataset: { workspaceInstanceId: `tab-${index}` },
        classList: classList(index === activeIndex ? 'active' : ''),
        style: { width: '' },
        getBoundingClientRect() { return { width: parseFloat(this.style.width) || naturalWidth }; }
    }));
    const items = tabs.map(tab => ({
        dataset: { workspaceOverflowInstanceId: tab.dataset.workspaceInstanceId },
        classList: classList()
    }));
    const list = {
        style: {},
        clientWidth: available,
        scrollLeft: 0,
        querySelectorAll: selector => selector === ':scope > .workspace-tab' ? tabs : []
    };
    const strip = {
        classList: classList(),
        querySelector: selector => selector === ':scope > .workspace-tab-list' ? list : null,
        querySelectorAll: selector => selector === '[data-workspace-overflow-instance-id]' ? items : []
    };
    const group = Object.assign(new Element(), {
        isConnected: true,
        querySelector: selector => selector === ':scope > .workspace-tabs' ? strip : null
    });
    return { group, strip, tabs, items };
}

test('keeps all tabs visible when they fit', () => {
    const f = fixture([60, 60, 60], 190, 1);
    tabsModule.initialize(f.group);
    assert.equal(f.strip.classList.contains('has-overflow'), false);
    assert.equal(f.tabs.some(tab => tab.classList.contains('tab-overflowed')), false);
    assert.equal(f.tabs.every(tab => tab.style.width === ''), true);
});

test('shrinks tabs by up to 25 percent before enabling overflow', () => {
    const f = fixture([100, 100], 170);
    tabsModule.initialize(f.group);
    assert.equal(f.strip.classList.contains('has-overflow'), false);
    assert.equal(f.tabs[0].style.width, '84px');
    assert.equal(f.tabs[1].style.width, '84px');
});

test('clips trailing tabs beneath overflow control without removing them', () => {
    const f = fixture([100, 100, 100], 190);
    tabsModule.initialize(f.group);
    assert.equal(f.strip.classList.contains('has-overflow'), true);
    assert.equal(f.tabs.every(tab => tab.style.width === '75px'), true);
    assert.equal(f.tabs[0].classList.contains('tab-overflowed'), false);
    assert.equal(f.tabs[1].classList.contains('tab-overflowed'), false);
    assert.equal(f.tabs[2].classList.contains('tab-overflowed'), true);
    assert.equal(f.items[0].classList.contains('menu-item-hidden'), true);
    assert.equal(f.items[1].classList.contains('menu-item-hidden'), true);
    assert.equal(f.items[2].classList.contains('menu-item-hidden'), false);
});

test('scrolls an active overflow tab into view and exposes tabs scrolled off the left', () => {
    const f = fixture([100, 100, 100], 190, 2);
    tabsModule.initialize(f.group);
    assert.equal(f.strip.classList.contains('has-overflow'), true);
    assert.equal(f.group.querySelector(':scope > .workspace-tabs').querySelector(':scope > .workspace-tab-list').scrollLeft, 39);
    assert.equal(f.tabs[0].classList.contains('tab-overflowed'), true);
    assert.equal(f.tabs[2].classList.contains('tab-overflowed'), false);
    assert.equal(f.items[0].classList.contains('menu-item-hidden'), false);
    assert.equal(f.items[2].classList.contains('menu-item-hidden'), true);
});

test('recalculates visibility when the section is resized', () => {
    const f = fixture([60, 60, 60], 130, 0);
    tabsModule.initialize(f.group);
    assert.equal(f.strip.classList.contains('has-overflow'), true);
    f.group.querySelector(':scope > .workspace-tabs').querySelector(':scope > .workspace-tab-list').clientWidth = 190;
    tabsModule.initialize(f.group);
    assert.equal(f.strip.classList.contains('has-overflow'), false);
    assert.equal(f.tabs.some(tab => tab.classList.contains('tab-overflowed')), false);
});

test('defers expensive tab measurements until rapid resizing settles', async () => {
    const f = fixture([60, 60, 60], 190, 0);
    tabsModule.initialize(f.group);
    await Promise.resolve();
    const list = f.group.querySelector(':scope > .workspace-tabs').querySelector(':scope > .workspace-tab-list');
    list.clientWidth = 130;
    lastResizeObserver.callback();
    lastResizeObserver.callback();

    assert.equal(f.strip.classList.contains('has-overflow'), false);
    await new Promise(resolve => setTimeout(resolve, 110));
    assert.equal(f.strip.classList.contains('has-overflow'), true);
});

test('ignores stale and detached Blazor element references', () => {
    assert.doesNotThrow(() => tabsModule.initialize(null));
    assert.doesNotThrow(() => tabsModule.initialize({ isConnected: false, querySelector() {} }));
    assert.doesNotThrow(() => tabsModule.initialize({ isConnected: true }));
});
