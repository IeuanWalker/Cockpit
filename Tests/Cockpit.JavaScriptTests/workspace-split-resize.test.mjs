import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';

function fixture(axis = 'columns') {
    const calls = [];
    const listeners = () => ({ listeners: {}, addEventListener(name, callback) { this.listeners[name] = callback; } });
    const bodyClasses = new Set();
    const container = {
        style: {},
        originalStyle: 'initial-layout',
        classList: { contains: name => name === 'workspace-split' },
        getBoundingClientRect: () => ({ left: 100, top: 50, width: 500, height: 400 }),
        getAttribute: name => name === 'style' ? container.originalStyle : null,
        setAttribute: (name, value) => { if (name === 'style') container.restoredStyle = value; }
    };
    const dividerClasses = new Set();
    const divider = {
        parentElement: container,
        dataset: { workspaceSplitId: 'split-1', workspaceAxis: axis, workspaceSplitRatio: '0.4' },
        classList: { add: name => dividerClasses.add(name), remove: name => dividerClasses.delete(name) }
    };
    const root = Object.assign(listeners(), {
        contains: value => value === divider,
        setPointerCapture() {},
        hasPointerCapture: () => false
    });
    const win = listeners();
    const doc = Object.assign(listeners(), {
        getElementById: () => root,
        body: {
            style: {},
            classList: { toggle(name, active) { active ? bodyClasses.add(name) : bodyClasses.delete(name); } }
        }
    });
    vm.runInNewContext(fs.readFileSync(new URL('../../src/Cockpit/wwwroot/js/workspace-split-resize.js', import.meta.url), 'utf8'),
        { window: win, document: doc, AbortController, console });
    win.cockpit.initializeWorkspaceSplitResize('rightSidebar', {
        invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(); }
    });
    const event = (x, y) => ({
        target: { closest: () => divider }, pointerId: 7, button: 0, isPrimary: true,
        clientX: x, clientY: y, preventDefault() {}
    });
    return { calls, bodyClasses, container, dividerClasses, root, win, doc, event };
}

test('column divider previews and persists its ratio', () => {
    const f = fixture();
    f.root.listeners.pointerdown(f.event(300, 200));
    f.root.listeners.pointermove(f.event(475, 200));
    assert.equal(f.container.style.gridTemplateColumns, 'minmax(0,0.75fr) 4px minmax(0,0.25fr)');
    assert.equal(f.doc.body.style.cursor, 'e-resize');
    f.win.listeners.pointerup(f.event(475, 200));
    assert.deepEqual(f.calls[0], ['OnWorkspaceSplitResize', 'split-1', 0.75]);
    assert.equal(f.doc.body.style.cursor, '');
});

test('row divider uses vertical position and clamps the persisted ratio', () => {
    const f = fixture('rows');
    f.root.listeners.pointerdown(f.event(200, 200));
    f.root.listeners.pointermove(f.event(200, 500));
    assert.equal(f.container.style.gridTemplateRows, 'minmax(0,0.9fr) 4px minmax(0,0.09999999999999998fr)');
    assert.equal(f.doc.body.style.cursor, 'n-resize');
    f.win.listeners.pointerup(f.event(200, 500));
    assert.deepEqual(f.calls[0], ['OnWorkspaceSplitResize', 'split-1', 0.9]);
});

test('Escape cancels resize and restores the rendered layout', () => {
    const f = fixture();
    f.root.listeners.pointerdown(f.event(300, 200));
    f.root.listeners.pointermove(f.event(500, 200));
    f.doc.listeners.keydown({ key: 'Escape' });
    assert.equal(f.container.restoredStyle, 'initial-layout');
    assert.equal(f.calls.length, 0);
    assert.equal(f.dividerClasses.has('resizing'), false);
});
