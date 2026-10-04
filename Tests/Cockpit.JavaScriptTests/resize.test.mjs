import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';

function fixture(side = 'left') {
    const calls = [];
    const frames = new Map();
    const overlays = [];
    let nextFrame = 1;
    const listeners = () => ({
        listeners: new Map(),
        addEventListener(name, handler) { this.listeners.set(name, handler); },
        removeEventListener(name, handler) {
            if (this.listeners.get(name) === handler) this.listeners.delete(name);
        }
    });
    const handle = Object.assign(listeners(), {
        isConnected: true,
        classList: { add() {}, remove() {} }
    });
    const sidebar = { isConnected: true, style: {} };
    const body = {
        style: {},
        appendChild(element) {
            overlays.push(element);
        }
    };
    const document = Object.assign(listeners(), {
        body,
        getElementById: id => id === 'handle' ? handle : id === 'sidebar' ? sidebar : null,
        createElement: () => ({
            style: {},
            setAttribute() {},
            remove() { this.removed = true; }
        })
    });
    const window = Object.assign(listeners(), {
        innerWidth: 1000,
        requestAnimationFrame(callback) {
            const id = nextFrame++;
            frames.set(id, callback);
            return id;
        },
        cancelAnimationFrame(id) { frames.delete(id); }
    });
    vm.runInNewContext(fs.readFileSync(new URL('../../src/Cockpit/wwwroot/js/resize.js', import.meta.url), 'utf8'),
        { window, document, console, WeakMap, Map, Object, Number, Math });
    window.cockpit.initializeResize('handle', 'sidebar', side, {
        invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(); }
    });
    const mouse = (clientX, button = 0) => ({ clientX, button, preventDefault() {} });
    return {
        calls, frames, handle, sidebar, document, window, overlays, mouse,
        runFrame() {
            const [id, callback] = frames.entries().next().value;
            frames.delete(id);
            callback();
        }
    };
}

test('sidebar resize stays in the DOM during movement and persists only once on release', () => {
    const f = fixture();
    f.handle.listeners.get('mousedown')(f.mouse(220));
    f.document.listeners.get('mousemove')(f.mouse(260));
    f.document.listeners.get('mousemove')(f.mouse(280));

    assert.equal(f.calls.length, 0);
    assert.equal(f.sidebar.style.width, undefined);
    assert.equal(f.frames.size, 1);
    f.runFrame();
    assert.equal(f.sidebar.style.width, '280px');
    assert.equal(f.calls.length, 0);

    f.document.listeners.get('mousemove')(f.mouse(305));
    f.document.listeners.get('mouseup')();
    assert.equal(f.sidebar.style.width, '305px');
    assert.deepEqual(f.calls, [['OnResize', 305]]);
    assert.equal(f.overlays[0].removed, true);
});

test('right sidebar width is measured from the viewport edge and clamped', () => {
    const f = fixture('right');
    f.handle.listeners.get('mousedown')(f.mouse(750));
    f.document.listeners.get('mousemove')(f.mouse(925));
    f.runFrame();

    assert.equal(f.sidebar.style.width, '150px');
    f.document.listeners.get('mouseup')();

    assert.equal(f.sidebar.style.width, '150px');
    assert.deepEqual(f.calls, [['OnResize', 150]]);
});
