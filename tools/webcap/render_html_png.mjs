import { chromium } from 'playwright';
const [,, src, out, w, h] = process.argv;
const b = await chromium.launch();
const p = await b.newPage({ viewport: { width: +w, height: +h }, deviceScaleFactor: 2 });
await p.goto('file:///' + src.split('\\').join('/'));
await p.waitForTimeout(500);
await p.screenshot({ path: out, clip: { x: 0, y: 0, width: +w, height: +h } });
await b.close();
console.log('ok', out);
