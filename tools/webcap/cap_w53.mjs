// SW 라이선스 화면(w53) 단독 재캡처 — 임시 서버 5599
import { chromium } from 'playwright';
const BASE = process.env.WEB_BASE || 'http://localhost:5599';
const OUT = process.env.WEB_OUT || 'D:/Temp/mancap/web1104/out/w53';
import fs from 'fs';
fs.mkdirSync(OUT, { recursive: true });
const browser = await chromium.launch();
const page = await (await browser.newContext({ viewport: { width: 1600, height: 1000 }, locale: 'ko-KR' })).newPage();
await page.goto(BASE + '/login', { waitUntil: 'networkidle' });
await page.waitForSelector('input', { timeout: 60000 });
await page.locator('input:not([type=password])').first().fill('admin');
await page.locator('input[type=password]').first().fill(process.env.WEB_PASS || 'Manual2026!');
await page.keyboard.press('Enter');
await page.waitForTimeout(5000);
await page.goto(BASE + '/admin/license', { waitUntil: 'networkidle', timeout: 60000 });
await page.waitForTimeout(4000);
await page.screenshot({ path: `${OUT}/w53_admin_license.png`, fullPage: true });
console.log('shot', page.url());
await browser.close();
