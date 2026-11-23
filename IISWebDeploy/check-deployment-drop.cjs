// Run: node IISWebDeploy/check-deployment-drop.cjs
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { runInNewContext } = require('node:vm');
const { File } = require('node:buffer');

const listeners = new Map();
runInNewContext(readFileSync(`${__dirname}/wwwroot/js/deployment-drop.js`, 'utf8'), {
    document: {
        addEventListener(type, handler) {
            assert.equal(listeners.has(type), false, 'one delegated handler per event');
            listeners.set(type, handler);
        }
    },
    Event
});
assert.equal(listeners.size, 4);

function fixture(enabled = true) {
    const classes = new Set();
    const input = {
        files: null,
        changes: 0,
        dispatchEvent(event) {
            assert.equal(event.type, 'change');
            assert.equal(event.bubbles, true);
            assert.ok(this.files[0] instanceof File, 'forward actual File objects, not filenames');
            this.changes++;
        }
    };
    const card = {
        classList: {
            remove: value => classes.delete(value),
            toggle: (value, on) => on ? classes.add(value) : classes.delete(value)
        },
        contains: target => target === child,
        querySelector(selector) {
            assert.equal(selector, 'input[type="file"]:not(:disabled)');
            return enabled ? input : null;
        }
    };
    const child = { closest: selector => {
        assert.equal(selector, '[data-deployment-drop]');
        return card;
    } };
    return { card, child, input, classes };
}
function fire(type, target, types = ['Files'], files = [], relatedTarget = null) {
    let active = true;
    const transfer = { types, dropEffect: '' };
    Object.defineProperty(transfer, 'files', { get() {
        assert.equal(active, true, 'FileList must be read synchronously');
        return files;
    } });
    const event = { target, relatedTarget, dataTransfer: transfer, prevented: false,
        preventDefault() { this.prevented = true; } };
    listeners.get(type)(event);
    active = false;
    return event;
}

const files = [new File(['zip contents'], 'build.zip', { type: 'application/zip' })];
const first = fixture();
assert.equal(fire('dragenter', first.child).dataTransfer.dropEffect, 'copy');
assert.ok(first.classes.has('drop-active'));
fire('dragleave', first.child, [], [], first.child);
assert.ok(first.classes.has('drop-active'), 'moving inside card preserves highlight');
fire('dragleave', first.child);
assert.equal(first.classes.size, 0);
fire('dragover', first.child);
assert.equal(fire('drop', first.child, ['Files'], files).prevented, true);
assert.equal(first.input.files, files, 'assign original native FileList');
assert.equal(first.input.changes, 1);
assert.equal(first.classes.size, 0);
fire('drop', first.child); // Empty and non-file drops must not upload.
fire('drop', first.child, ['text/plain'], files);
assert.equal(first.input.changes, 1);
for (let refresh = 0; refresh < 3; refresh++) {
    const replacement = fixture();
    fire('drop', replacement.child, ['Files'], files);
    assert.equal(replacement.input.changes, 1, 'replacement cards need no registration');
}
const blocked = fixture(false); // Busy/unavailable cards have no enabled InputFile.
assert.equal(fire('dragover', blocked.child).dataTransfer.dropEffect, 'none');
assert.equal(fire('drop', blocked.child, ['Files'], files).prevented, true);
assert.equal(blocked.input.changes, 0);
assert.equal(blocked.classes.size, 0);
fire('drop', { closest: () => null }, ['Files'], files);

const home = readFileSync(`${__dirname}/Pages/Home.razor`, 'utf8');
const app = readFileSync(`${__dirname}/App.razor`, 'utf8');
assert.match(home, /data-deployment-drop/);
assert.match(home, /site.TargetAvailable && !parentBusy/);
assert.match(home, /OnChange="e => Upload\(site, e\)"/);
assert.doesNotMatch(home, /deploymentDrop|@ondrop|_dropDepth/);
assert.equal(app.match(/type="module" src="js\/deployment-drop.js"/g)?.length, 1);
console.log('PASS: native synchronous file forwarding, bubbling change, highlight, guards, replacement cards, and app-shell loading');
