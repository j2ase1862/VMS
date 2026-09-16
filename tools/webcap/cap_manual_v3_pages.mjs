// BODA.VMS.Web 매뉴얼용 전체 페이지 캡처 (임시 서버 5599, 데모 DB)
import { chromium } from 'playwright';
import fs from 'fs';
const BASE = process.env.WEB_BASE || 'http://localhost:5599';
const OUT = process.env.WEB_OUT || 'D:/Temp/mancap/web/shots';
const USER = 'admin', PASS = process.env.WEB_PASS || 'Manual2026!';
fs.mkdirSync(OUT, { recursive: true });

const browser = await chromium.launch();
const ctx = await browser.newContext({ viewport: { width: 1600, height: 1000 }, locale: 'ko-KR' });
const page = await ctx.newPage();
const log = (m) => { console.log(m); fs.appendFileSync(OUT + '/_log.txt', m + '\n'); };

async function shot(name, opts = {}) {
  await page.waitForTimeout(opts.wait ?? 2500);
  await page.screenshot({ path: `${OUT}/${name}.png`, fullPage: opts.full ?? true });
  log(`shot ${name} ${page.url()}`);
}

// 1. 로그인 화면 (미로그인)
await page.goto(BASE + '/login', { waitUntil: 'networkidle' });
await page.waitForSelector('input', { timeout: 60000 });
await shot('w00_login', { full: false });
// 회원가입 화면
await page.goto(BASE + '/register', { waitUntil: 'networkidle' });
await shot('w01_register', { full: false });

// 로그인
await page.goto(BASE + '/login', { waitUntil: 'networkidle' });
await page.waitForSelector('input', { timeout: 60000 });
await page.locator('input:not([type=password])').first().fill(USER);
await page.locator('input[type=password]').first().fill(PASS);
await page.keyboard.press('Enter');
await page.waitForTimeout(5000);
log('after login url=' + page.url());

const routes = [
  ['w10_dashboard', '/'],
  ['w11_andon', '/andon'],
  ['w12_alarms', '/alarms'],
  ['w13_history', '/history'],
  ['w20_workorders', '/workorders'],
  ['w21_products', '/products'],
  ['w22_operators', '/operators'],
  ['w30_inspection_items', '/inspection-items'],
  ['w31_quality_analysis', '/quality-analysis'],
  ['w32_spc', '/spc'],
  ['w33_defect_codes', '/defect-codes'],
  ['w34_shift_report', '/shift-report'],
  ['w35_reports', '/reports'],
  ['w40_clients', '/clients'],
  ['w41_oee', '/oee'],
  ['w42_reliability', '/reliability'],
  ['w43_maintenance', '/maintenance'],
  ['w44_forecast', '/forecast'],
  ['w50_shifts', '/shifts'],
  ['w51_admin_approvals', '/admin/approvals'],
  ['w52_audit_logs', '/audit-logs'],
  ['w53_admin_license', '/admin/license'],
  ['w54_settings', '/settings'],
  ['w55_change_password', '/change-password'],
  ['w60_kiosk', '/kiosk/11'],
];
for (const [name, route] of routes) {
  try {
    await page.goto(BASE + route, { waitUntil: 'networkidle', timeout: 60000 });
    await shot(name, { wait: 4000 });
  } catch (e) { log(`FAIL ${name}: ${e.message}`); }
}

// 알림 종 드롭다운 (대시보드에서)
try {
  await page.goto(BASE + '/', { waitUntil: 'networkidle' });
  await page.waitForTimeout(3000);
  const bell = page.locator('button:has(svg), button.mud-icon-button').filter({ hasText: '' });
  const cand = page.locator('[aria-label*="알림"], [title*="알림"], button:has(.mud-badge)').first();
  if (await cand.count()) { await cand.click(); await shot('w70_notification_bell', { full: false }); }
  else log('bell not found');
} catch (e) { log('bell err ' + e.message); }

await browser.close();
log('DONE');
