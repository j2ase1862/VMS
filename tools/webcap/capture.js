// BODA.VMS.Web 데모 화면 캡처 (Playwright + 설치된 Edge). 다크모드, 1920x1080.
const { chromium } = require('playwright');
const path = require('path');

const BASE = process.env.WEB_BASE || 'http://localhost:5393';
const USER = process.env.WEB_USER || 'admin';
const PASS = process.env.WEB_PASS || 'DemoPass2026!';
const OUT = process.env.WEB_OUT || 'D:\\Repo\\VMS\\docs\\promo\\web';

async function shot(page, name, ms = 2200) {
  await page.waitForLoadState('networkidle').catch(() => {});
  await page.waitForTimeout(ms);
  await page.screenshot({ path: path.join(OUT, name + '.png') });
  console.log('  shot', name);
}

(async () => {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  const ctx = await browser.newContext({
    viewport: { width: 1920, height: 1080 }, deviceScaleFactor: 1,
    ignoreHTTPSErrors: true, locale: 'ko-KR',
  });
  // 다크모드 강제 (Blazored.LocalStorage darkMode=true)
  await ctx.addInitScript(() => { try { localStorage.setItem('darkMode', 'true'); } catch (e) {} });
  const page = await ctx.newPage();

  // 로그인
  await page.goto(BASE + '/login', { waitUntil: 'networkidle' });
  await page.waitForTimeout(1200);
  try {
    await page.getByLabel('Username').fill(USER);
    await page.getByLabel('Password').fill(PASS);
  } catch {
    await page.locator('input').first().fill(USER);
    await page.locator('input[type=password]').first().fill(PASS);
  }
  await page.getByRole('button', { name: /Sign In/i }).click();
  await page.waitForURL(u => !u.toString().includes('/login'), { timeout: 15000 }).catch(() => {});
  await page.waitForTimeout(2800);
  console.log('logged in (dark), url=', page.url());

  const nav = async (route, name, ms) => {
    await page.goto(BASE + route, { waitUntil: 'networkidle' }).catch(() => {});
    await shot(page, name, ms);
  };

  await nav('/', '01_dashboard', 2800);
  await nav('/history', '02_history', 2200);
  await nav('/workorders', '03_workorders', 2000);
  await nav('/quality-analysis', '04_pareto', 3000);
  await nav('/reliability', '06_reliability', 3000);
  await nav('/alarms', '08_alarms', 2200);
  await nav('/maintenance', '09_maintenance', 2200);
  await nav('/defect-codes', '10_defectcodes', 2000);
  await nav('/products', '12_products', 2000);
  await nav('/operators', '13_operators', 2000);

  // ── OEE: 기본 today 범위는 KST/UTC 차이로 0% → 시작일을 7일 전으로 변경 후 계산 ──
  try {
    await page.goto(BASE + '/oee', { waitUntil: 'networkidle' });
    await page.waitForTimeout(1500);
    const startField = page.locator('.mud-input-control', { hasText: /시작일|Start/ }).first();
    await startField.click();
    await page.waitForTimeout(700);
    // 현재 달력에서 9일 클릭 (오늘 16일 기준 7일 전)
    await page.locator('.mud-picker-calendar-day', { hasText: /^9$/ }).first().click();
    await page.waitForTimeout(500);
    await page.getByRole('button', { name: /계산|Calculate/ }).click().catch(() => {});
    await shot(page, '05_oee', 3000);
  } catch (e) {
    console.log('  OEE date-set failed:', e.message);
    await shot(page, '05_oee', 1500);
  }

  // ── SPC: Line → Recipe → Parameter 선택 후 계산 ──
  try {
    await page.goto(BASE + '/spc', { waitUntil: 'networkidle' });
    await page.waitForTimeout(1500);
    const pick = async (labelRe) => {
      await page.locator('.mud-select', { hasText: labelRe }).first().click()
        .catch(async () => { await page.locator('.mud-input-control', { hasText: labelRe }).first().click(); });
      await page.waitForSelector('[role="option"]', { timeout: 5000 });
      await page.waitForTimeout(400);
      await page.locator('[role="option"]').first().click();
      await page.waitForTimeout(1100);
    };
    await pick(/라인|Line/);
    await pick(/레시피|Recipe/);
    await pick(/파라미터|Parameter/);
    await page.getByRole('button', { name: /계산|Calculate/ }).click().catch(() => {});
    await shot(page, '11_spc', 3200);
  } catch (e) {
    console.log('  SPC failed:', e.message);
  }

  await browser.close();
  console.log('done.');
})().catch(e => { console.error('FATAL', e); process.exit(1); });
