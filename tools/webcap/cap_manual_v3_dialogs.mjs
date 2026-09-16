// BODA.VMS.Web 다이얼로그·상호작용 화면 2차 캡처
import { chromium } from 'playwright';
import fs from 'fs';
const BASE = 'http://localhost:5599';
const OUT = 'D:/Temp/mancap/web/shots2';
fs.mkdirSync(OUT, { recursive: true });
const browser = await chromium.launch();
const ctx = await browser.newContext({ viewport: { width: 1600, height: 1000 }, locale: 'ko-KR' });
const page = await ctx.newPage();
const log = (m) => { console.log(m); fs.appendFileSync(OUT + '/_log.txt', m + '\n'); };
async function shot(name, wait = 1500) { await page.waitForTimeout(wait); await page.screenshot({ path: `${OUT}/${name}.png` }); log('shot ' + name); }
async function goto(r) { await page.goto(BASE + r, { waitUntil: 'networkidle', timeout: 60000 }); await page.waitForTimeout(2500); }
async function clickText(t) { const l = page.getByRole('button', { name: t }).first(); await l.waitFor({ timeout: 8000 }); await l.click(); }
async function closeDialog() { await page.keyboard.press('Escape'); await page.waitForTimeout(600); }
async function step(name, fn) { try { await fn(); } catch (e) { log(`FAIL ${name}: ${e.message.split('\n')[0]}`); await closeDialog(); } }

await goto('/login');
await page.waitForSelector('input', { timeout: 60000 });
await page.locator('input:not([type=password])').first().fill('admin');
await page.locator('input[type=password]').first().fill('Manual2026!');
await page.keyboard.press('Enter'); await page.waitForTimeout(5000);

// 작업지시 등록 / LOT
await step('wo_form', async () => { await goto('/workorders'); await clickText('작업 지시 등록'); await shot('d10_wo_form'); await closeDialog(); });
await step('wo_lot', async () => { await goto('/workorders'); await page.getByRole('button', { name: 'LOT' }).first().click(); await shot('d11_wo_lot'); await closeDialog(); });
// 생산 이력 상세
await step('history_detail', async () => { await goto('/history'); await page.getByRole('button', { name: '상세' }).first().click(); await shot('d12_history_detail', 2500); await closeDialog(); });
// 제품 등록
await step('product_form', async () => { await goto('/products'); await clickText('제품 등록'); await shot('d13_product_form'); await closeDialog(); });
// 작업자 등록 + 탭
await step('operator_form', async () => { await goto('/operators'); await clickText('작업자 등록'); await shot('d14_operator_form'); await closeDialog(); });
await step('operator_sessions', async () => { await goto('/operators'); await page.getByRole('tab', { name: /출근 이력/ }).click(); await shot('d15_operator_sessions', 2500); });
await step('operator_kiosk', async () => { await page.getByRole('tab', { name: /키오스크/ }).click(); await shot('d16_operator_kiosk_links', 1500); });
// 레시피 파라미터: 라인 선택 → 레시피 선택
await step('inspection_items', async () => {
  await goto('/inspection-items');
  const selects = page.locator('.mud-select');
  await selects.nth(0).click(); await page.waitForTimeout(600);
  await page.locator('.mud-list-item').nth(0).click(); await page.waitForTimeout(1500);
  await selects.nth(1).click(); await page.waitForTimeout(600);
  await page.locator('.mud-list-item').nth(0).click(); await page.waitForTimeout(2500);
  await shot('d20_inspection_items_selected');
  await step('preset', async () => { await clickText('프리셋 추가'); await page.waitForTimeout(600); await page.locator('.mud-list-item').first().click(); await shot('d21_preset_preview'); await closeDialog(); });
  await step('param_form', async () => { await clickText('항목 추가'); await shot('d22_param_form'); await closeDialog(); });
});
// 알람 해제 다이얼로그
await step('alarm_resolve', async () => { await goto('/alarms'); await page.getByRole('button', { name: '해제' }).first().click(); await shot('d23_alarm_resolve'); await closeDialog(); });
// 불량 코드 등록
await step('defect_form', async () => { await goto('/defect-codes'); await clickText('코드 등록'); await shot('d24_defect_form'); await closeDialog(); });
// SPC: 라인 → 레시피 → 파라미터 → 계산
await step('spc', async () => {
  await goto('/spc');
  const selects = page.locator('.mud-select');
  for (let i = 0; i < 3; i++) { await selects.nth(i).click(); await page.waitForTimeout(600); const items = page.locator('.mud-list-item'); const n = await items.count(); await items.nth(n > 1 ? 1 : 0).click(); await page.waitForTimeout(1500); }
  await clickText('계산'); await shot('d25_spc_result', 3500);
});
// 예방 보전 등록 / 수행 기록
await step('pm_form', async () => { await goto('/maintenance'); await clickText('일정 등록'); await shot('d26_pm_form'); await closeDialog(); });
await step('pm_perform', async () => { await goto('/maintenance'); await page.getByRole('button', { name: '수행 기록' }).first().click(); await shot('d27_pm_perform'); await closeDialog(); });
// 사용자 승인 전체 탭
await step('users_all', async () => { await goto('/admin/approvals'); await page.getByRole('tab', { name: /전체 사용자/ }).click(); await shot('d28_users_all', 2000); });
// 클라이언트 등록
await step('client_form', async () => { await goto('/clients'); await clickText('클라이언트 등록'); await shot('d29_client_form'); await closeDialog(); });
// 교대 등록
await step('shift_form', async () => { await goto('/shifts'); await clickText('교대 등록'); await shot('d30_shift_form'); await closeDialog(); });
// 다크 모드 대시보드
await step('dark', async () => { await goto('/'); const tg = page.locator('button[aria-label*="dark" i], button:has(svg[data-testid*="DarkMode"])').first(); if (await tg.count()) { await tg.click(); await shot('d31_dashboard_dark', 2500); await tg.click(); } else log('dark toggle not found'); });
// 모바일 드로어
await step('mobile', async () => { await page.setViewportSize({ width: 480, height: 900 }); await goto('/'); await page.locator('button').first().click(); await shot('d32_mobile_drawer', 1500); await page.setViewportSize({ width: 1600, height: 1000 }); });
await browser.close(); log('DONE');
