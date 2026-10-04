// Run with a local Playwright installation; PLAYWRIGHT_MODULE can point to its index.mjs.
// Uses real xterm rendering and a deterministic shell bridge, without touching user sessions.
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE ?? 'playwright');
const root = new URL('../../src/Cockpit.Tools.Terminal/wwwroot/', import.meta.url);
const server = createServer(async (req, res) => {
    if (req.url === '/') { res.setHeader('Content-Type', 'text/html'); res.end('<html><body><div id="one" style="width:700px;height:340px"></div><div id="two" style="width:450px;height:200px"></div></body></html>'); return; }
    try {
        const path = new URL('.' + req.url, root);
        if (!path.href.startsWith(root.href)) { res.writeHead(403).end(); return; }
        res.setHeader('Content-Type', path.pathname.endsWith('.css') ? 'text/css' : 'text/javascript');
        res.end(await readFile(path));
    } catch { res.writeHead(404).end(); }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const browser = await chromium.launch({ channel: 'msedge', headless: true });
try {
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    await page.goto(`http://127.0.0.1:${server.address().port}/`);
    await page.evaluate(async () => {
        window.api = await import('/terminal.js');
        window.inputs = []; window.sizes = []; window.pending = ''; window.failures = [];
        window.bridge = { async invokeMethodAsync(method, ...args) {
            if (method === 'TakeOutput') { const data = pending; pending = ''; return data; }
            if (method === 'Input') { inputs.push(args[0]); return; }
            if (method === 'ReportError') { failures.push(args[0]); return; }
            if (method === 'Start' || method === 'Restart') { sizes.push(args); pending += 'shell> '; return; }
            if (method === 'Resize') {
                sizes.push(args);
                await new Promise(resolve => setTimeout(resolve, 10));
                pending += '\r\x1b[2Kshell> ';
            }
        }};
        await api.attach('one', document.querySelector('#one'), 'a', bridge, '', 120, 30, 22621);
        await api.attach('two', document.querySelector('#two'), 'b', {
            async invokeMethodAsync(method) { return method === 'TakeOutput' ? '' : undefined; }
        }, '', 80, 24, null);
    });
    assert.ok((await page.evaluate(() => sizes[0][0])) < 120, 'shell starts with measured width');
    await page.locator('#one textarea').focus();
    await page.keyboard.type('echo hello');
    await page.keyboard.press('ArrowUp');
    await page.keyboard.press('Control+c');
    await page.waitForFunction(() => inputs.includes('\u0003'));
    assert.ok((await page.evaluate(() => inputs.join(''))).includes('\u001b[A'), 'arrow keys reach PTY');
    await page.evaluate(async () => {
        pending += '\r\n\x1b[31mRED\x1b[0m\r\nprogress 10%\r\x1b[2Kprogress 100%\r\nshell> ';
        await api.flush('one');
    });
    assert.match(await page.evaluate(() => api.text('one', 50)), /RED\nprogress 100%/);
    assert.equal(await page.evaluate(() => api.messageText('one', 50)), 'shell> \nRED\nprogress 100%');
    await page.evaluate(async () => {
        pending += '\r\x1b[2KPS D:\\WS\\Github-Mine\\Cockpit> ';
        await api.flush('one');
    });
    assert.equal((await page.evaluate(() => api.messageText('one', 50))).endsWith('progress 100%'), true, 'idle PowerShell prompt excluded');
    await page.evaluate(async () => {
        pending += '\r\x1b[2Kordinary output without newline';
        await api.flush('one');
    });
    assert.ok((await page.evaluate(() => api.messageText('one', 50))).endsWith('ordinary output without newline'), 'last real output preserved');
    const beforeNode = await page.locator('#one .xterm').elementHandle();
    for (const width of [260, 900, 330, 780, 420, 700]) {
        await page.evaluate(width => { document.querySelector('#one').style.width = `${width}px`; }, width);
        await page.waitForTimeout(25);
    }
    await page.waitForTimeout(120);
    assert.ok((await page.evaluate(() => sizes.length)) > 2, 'resize observer drives shell resizing');
    await page.evaluate(async () => {
        api.detach('one', 'a');
        pending += '\r\nBACKGROUND\r\n';
        await api.flush('one');
        await api.attach('one', document.querySelector('#one'), 'c', bridge, '', 120, 30, 22621);
        api.detach('one', 'a'); // stale Razor disposal must not detach the new attachment
    });
    assert.equal(await beforeNode.evaluate(node => node.isConnected), true, 'same emulator survives tab switching');
    assert.equal((await page.evaluate(() => api.text('one', 50))).split('BACKGROUND').length, 2, 'output delivered once');
    const snapshot = await page.evaluate(() => api.snapshot('one'));
    assert.ok(snapshot.includes('BACKGROUND'));
    await page.evaluate(async saved => {
        await api.dispose('one');
        await api.attach('one', document.querySelector('#one'), 'd', bridge, saved, 80, 24, 22621);
    }, snapshot);
    assert.match(await page.evaluate(() => api.text('one', 50)), /BACKGROUND/);
    await page.evaluate(() => api.reset('one'));
    assert.equal((await page.evaluate(() => api.text('one', 50))).includes('BACKGROUND'), false);
    assert.equal(await page.locator('#two .xterm').count(), 1, 'reset does not affect other terminals');
    assert.deepEqual(await page.evaluate(() => failures), []);
    assert.deepEqual(errors, []);
    // Check the actual popup markup/styles against the workspace fill rule that
    // previously stretched dialogs to the entire window. Razor values are fixture data.
    const razor = await readFile(new URL('../TerminalTool.razor', root), 'utf8');
    const popup = razor.match(/<dialog[\s\S]*?<\/dialog>/)[0]
        .replace(/<pre>[\s\S]*?<\/pre>/, '<pre>file-one.txt\nfile-two.txt</pre>')
        .replace('@_previewLineCount', '2')
        .replace(/@[\w:.]+(?:="[^"]*")?/g, '')
        .replace(/disabled="[^"]*"/g, '');
    const css = await readFile(new URL('../TerminalTool.razor.css', root), 'utf8');
    const workspaceCss = await readFile(new URL('../../Cockpit/Components/Pages/ToolPanel/WorkspaceNodeView.razor.css', root), 'utf8');
    await page.addStyleTag({ content: css + '\n' + workspaceCss.replaceAll('::deep', '') });
    await page.evaluate(markup => {
        const container = document.createElement('div');
        container.className = 'workspace-tool-content';
        container.innerHTML = markup;
        document.body.append(container);
        api.showDialog(container.querySelector('dialog'));
    }, popup);
    const bounds = await page.locator('dialog').boundingBox();
    assert.equal(Math.round(bounds.width), 500, 'popup retains legacy width inside workspace');
    assert.ok(bounds.height < page.viewportSize().height * .8, 'popup height fits its content');
    assert.ok(Math.abs(bounds.y + bounds.height / 2 - page.viewportSize().height / 2) < 2, 'popup is vertically centered');
    assert.equal(await page.locator('dialog .message-close').count(), 1);
    await page.evaluate(async () => { await api.dispose('one'); await api.dispose('two'); });
    console.log('Terminal browser checks passed: input, ANSI, resize, background output, remount, restore, reset and isolation.');
} finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
