import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';

function fixture() {
    const calls = [];
    const previews = [];
    const bodyClasses = new Set();
    const htmlClasses = new Set();
    const cssStyle = () => {
        const values = new Map();
        const priorities = new Map();
        return {
            setProperty: (name, value, priority = '') => { values.set(name, value); priorities.set(name, priority); },
            removeProperty: name => { values.delete(name); priorities.delete(name); },
            getPropertyValue: name => values.get(name) || '',
            getPropertyPriority: name => priorities.get(name) || ''
        };
    };
    const classList = values => ({
        add: name => values.add(name),
        remove: name => values.delete(name),
        toggle: (name, active) => active ? values.add(name) : values.delete(name)
    });
    const surface = () => ({ listeners: {}, addEventListener(name, fn) { this.listeners[name] = fn; } });
    const surfaceBounds = { left: 0, top: 40, width: 800, height: 400 };
    let toolPickerOpen = false;
    let contextMenuOpen = false;
    const root = Object.assign(surface(), { contains: () => true, setPointerCapture() {}, hasPointerCapture: () => false,
        style: cssStyle(), classList: classList(new Set()),
        querySelector: selector => selector === '.workspace-surface' ? { getBoundingClientRect: () => surfaceBounds }
            : selector === '.tool-picker' && toolPickerOpen ? {} : null });
    const win = surface();
    const group = { dataset: { workspaceGroupId: 'group', sectionCount: '1', tabCount: '2' },
        querySelector: () => ({ getBoundingClientRect: () => ({ left: 0, top: 40, width: 400, height: 400 }) }) };
    const tab = { dataset: { workspaceInstanceId: 'one' }, firstElementChild: { textContent: 'Terminal' },
        classList: { add() {}, remove() {} },
        getBoundingClientRect: () => ({ left: 0, top: 0, width: 100, height: 30 }),
        cloneNode: () => ({ style: {}, querySelector: () => null, remove() { this.removed = true; } }),
        closest: selector => selector === '.workspace-group' ? group : selector === '.workspace-tab' ? tab : null };
    let hit = { closest: selector => selector === '.workspace-group' ? group : null };
    const doc = Object.assign(surface(), { getElementById: () => root, elementFromPoint: () => hit,
        querySelector: selector => selector === '.workspace-tab-context-menu' && contextMenuOpen ? {} : null,
        createElement: () => ({ style: {}, remove() { this.removed = true; } }),
        documentElement: { style: cssStyle(), classList: classList(htmlClasses) },
        body: { style: cssStyle(), appendChild: element => previews.push(element), classList: classList(bodyClasses) } });
    vm.runInNewContext(fs.readFileSync(new URL('../../src/Cockpit/wwwroot/js/workspace-drag.js', import.meta.url), 'utf8'),
        { window: win, document: doc, AbortController, console });
    win.cockpit.initializeWorkspaceDrag('sidebar', { invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(); } });
    const event = (x, y) => ({ target: tab, pointerId: 1, button: 0, isPrimary: true, clientX: x, clientY: y, preventDefault() {} });
    return { calls, previews, bodyClasses, htmlClasses, group, root, win, doc, event, setHit: value => { hit = value; },
        setToolPickerOpen: value => { toolPickerOpen = value; },
        setContextMenuOpen: value => { contextMenuOpen = value; },
        start() { root.listeners.pointerdown(event(20, 20)); },
        move(x, y) { root.listeners.pointermove(event(x, y)); },
        up(x, y) { win.listeners.pointerup(event(x, y)); } };
}

test('pointer gesture previews and commits a right split without HTML drag events', () => {
    const f = fixture(); f.start(); f.move(780, 240);
    assert.equal(f.previews[0].style.left, '400px');
    assert.equal(f.previews[0].style.width, '400px');
    assert.equal(f.previews[1].style.left, '792px');
    assert.equal(f.bodyClasses.has('workspace-tab-dragging'), true);
    assert.equal(f.htmlClasses.has('workspace-tab-dragging'), true);
    assert.equal(f.root.style.getPropertyValue('cursor'), 'grabbing');
    assert.equal(f.previews[0].textContent, '');
    f.up(780, 240);
    assert.deepEqual(f.calls[0], ['DropWorkspaceTab', 'one', 'group', null, false, 'right']);
    assert.equal(f.previews[0].removed, true);
    assert.equal(f.bodyClasses.has('workspace-tab-dragging'), false);
    assert.equal(f.htmlClasses.has('workspace-tab-dragging'), false);
    assert.equal(f.root.style.getPropertyValue('cursor'), '');
});

test('click without movement does not reorder', () => {
    const f = fixture(); f.start(); f.up(20, 20);
    assert.equal(f.calls.length, 0);
});

test('Escape and release outside cancel without changing the layout', () => {
    const f = fixture(); f.start(); f.move(780, 240); f.doc.listeners.keydown({ key: 'Escape' }); f.up(780, 240);
    assert.equal(f.calls.length, 0);
    f.start(); f.move(780, 240); f.setHit(null); f.up(900, 240);
    assert.equal(f.calls.length, 0);
});

test('four sections prevent a fifth split but still allow moving into a group', () => {
    const f = fixture(); f.group.dataset.sectionCount = '4'; f.start(); f.move(780, 240); f.up(780, 240);
    assert.equal(f.calls[0][5], null);
});

test('top edge preview spans the entire panel when the hovered group is only half width', () => {
    const f = fixture(); f.group.dataset.sectionCount = '2'; f.start(); f.move(200, 60);
    assert.equal(f.previews[0].style.left, '0px');
    assert.equal(f.previews[0].style.width, '800px');
    assert.equal(f.previews[0].style.height, '200px');
    f.up(200, 60);
    assert.equal(f.calls[0][5], 'top');
});

test('dropping on the right half of a tab inserts after that tab', () => {
    const f = fixture();
    const anchor = { dataset: { workspaceInstanceId: 'two' }, getBoundingClientRect: () => ({ left: 100, right: 200, top: 0, width: 100, height: 35 }) };
    f.setHit({ closest: selector => selector === '.workspace-group' ? f.group : selector === '.workspace-tab' ? anchor : {} });
    f.start(); f.move(180, 20); f.up(180, 20);
    assert.deepEqual(f.calls[0], ['DropWorkspaceTab', 'one', 'group', 'two', true, null]);
});

test('adjacent tabs share one insertion snap point', () => {
    const f = fixture();
    const second = { dataset: { workspaceInstanceId: 'two' }, getBoundingClientRect: () => ({ left: 102, right: 202, top: 0, width: 100, height: 35 }) };
    const third = { dataset: { workspaceInstanceId: 'three' }, getBoundingClientRect: () => ({ left: 204, right: 304, top: 0, width: 100, height: 35 }) };
    const strip = {
        getBoundingClientRect: () => ({ left: 0, right: 320, top: 0, width: 320, height: 35 }),
        querySelectorAll: () => [second, third]
    };
    f.setHit({ closest: selector => selector === '.workspace-group' ? f.group : selector === '.workspace-tabs' ? strip : second });

    f.start(); f.move(198, 20);
    assert.equal(f.previews[0].style.left, '201.5px');
    f.move(208, 20); f.up(208, 20);
    assert.equal(f.previews[0].style.left, '201.5px');
    assert.deepEqual(f.calls[0], ['DropWorkspaceTab', 'one', 'group', 'three', false, null]);
});

test('clicking outside an open tool picker requests dismissal', () => {
    const f = fixture();
    f.setToolPickerOpen(true);
    f.doc.listeners.pointerdown({ target: { closest: () => null } });
    assert.deepEqual(f.calls[0], ['DismissWorkspaceToolPicker']);
});

test('clicking the picker or its add button keeps it open', () => {
    const f = fixture();
    f.setToolPickerOpen(true);
    f.doc.listeners.pointerdown({ target: { closest: () => ({}) } });
    assert.equal(f.calls.length, 0);
});

test('clicking outside an open tab context menu requests dismissal', () => {
    const f = fixture();
    f.setContextMenuOpen(true);
    f.doc.listeners.pointerdown({ button: 0, target: { closest: () => null } });
    assert.deepEqual(f.calls[0], ['DismissWorkspaceTabContextMenu']);
});

test('clicking inside a tab context menu or right-clicking another tab keeps dismissal ordered', () => {
    const f = fixture();
    f.setContextMenuOpen(true);
    f.doc.listeners.pointerdown({ button: 0, target: { closest: selector => selector === '.workspace-tab-context-menu' ? {} : null } });
    f.doc.listeners.pointerdown({ button: 2, target: { closest: selector => selector === '.workspace-tab' ? {} : null } });
    assert.equal(f.calls.length, 0);
});
