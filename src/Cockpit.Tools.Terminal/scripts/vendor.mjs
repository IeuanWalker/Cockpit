import { copyFile, mkdir } from 'node:fs/promises';
const target = new URL('../wwwroot/vendor/', import.meta.url);
await mkdir(target, { recursive: true });
for (const [source, name] of [
    ['xterm/lib/xterm.js', 'xterm.js'], ['xterm/css/xterm.css', 'xterm.css'],
    ['xterm/LICENSE', 'xterm.LICENSE'],
    ['addon-fit/lib/addon-fit.js', 'addon-fit.js'], ['addon-fit/LICENSE', 'addon-fit.LICENSE'],
    ['addon-serialize/lib/addon-serialize.js', 'addon-serialize.js'], ['xterm/LICENSE', 'addon-serialize.LICENSE']
]) await copyFile(new URL(`../node_modules/@xterm/${source}`, import.meta.url), new URL(name, target));
