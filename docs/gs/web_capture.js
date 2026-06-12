// BODA.VMS.Web 스크린샷 캡처 — Chrome CDP (Node 24 내장 fetch/WebSocket, npm 불필요)
// phase=login : 로그인 검증 + 랜딩 캡처 + 사이드 메뉴 경로 추출
const { spawn } = require("child_process");
const fs = require("fs");
const os = require("os");
const path = require("path");

const CHROME = "C:/Program Files/Google/Chrome/Application/chrome.exe";
const PORT = 9222;
const BASE = "http://localhost:5292";
const SHOT = "D:/Repo/VMS/docs/gs/screenshots";
const USER = "admin", PASS = "admin";
const userDir = path.join(os.tmpdir(), "vmsweb_" + Date.now());

const sleep = (ms) => new Promise(r => setTimeout(r, ms));

async function main() {
  const chrome = spawn(CHROME, [
    "--headless=new", "--disable-gpu", "--hide-scrollbars",
    `--remote-debugging-port=${PORT}`, `--user-data-dir=${userDir}`,
    "--window-size=1600,1000", "--no-first-run", "--no-default-browser-check",
    BASE,
  ], { detached: false });
  chrome.on("error", e => console.log("chrome spawn err", e.message));

  // wait for CDP endpoint
  let wsUrl = null;
  for (let i = 0; i < 40; i++) {
    await sleep(500);
    try {
      const r = await fetch(`http://127.0.0.1:${PORT}/json`);
      const targets = await r.json();
      const page = targets.find(t => t.type === "page");
      if (page && page.webSocketDebuggerUrl) { wsUrl = page.webSocketDebuggerUrl; break; }
    } catch {}
  }
  if (!wsUrl) { console.log("NO CDP ENDPOINT"); chrome.kill(); return; }

  const ws = new WebSocket(wsUrl);
  let idc = 0; const pending = new Map();
  ws.addEventListener("message", ev => {
    const m = JSON.parse(ev.data);
    if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); }
  });
  await new Promise((res, rej) => { ws.addEventListener("open", res); ws.addEventListener("error", rej); });
  const send = (method, params = {}) => new Promise(res => { const i = ++idc; pending.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
  const eval = async (expr) => { const r = await send("Runtime.evaluate", { expression: `(function(){${expr}})()`, returnByValue: true, awaitPromise: true }); return r.result && r.result.result ? r.result.result.value : undefined; };
  const shot = async (file) => { const r = await send("Page.captureScreenshot", { format: "png" }); if (r.result && r.result.data) fs.writeFileSync(path.join(SHOT, file), Buffer.from(r.result.data, "base64")); };
  async function waitFor(cond, ms) { const t = Date.now(); while (Date.now() - t < ms) { if (await eval(`return (${cond})`)) return true; await sleep(400); } return false; }

  await send("Page.enable"); await send("Runtime.enable");

  // WASM 렌더 대기(스플래시 종료)
  await waitFor("!document.body.innerText.includes('VISION MANAGEMENT SYSTEM')", 35000);
  // 로그인 페이지로 이동 후 폼 대기
  await send("Page.navigate", { url: BASE + "/login" });
  const formReady = await waitFor("document.querySelectorAll('input').length>=1 && !document.body.innerText.includes('VISION MANAGEMENT SYSTEM')", 30000);
  await sleep(800);
  console.log("login form ready:", formReady, "| url:", await eval("return location.href"), "| inputs:", await eval("return document.querySelectorAll('input').length"));
  await shot("40_web_login.png");

  // 자격증명 입력 — CDP Input.insertText (실제 타이핑처럼 → MudBlazor 바인딩 정상)
  await eval("const i=[...document.querySelectorAll('input')];const p=i.find(x=>x.type==='password');const u=i.find(x=>x!==p);window.__u=u;window.__p=p;u.focus();return 1;");
  await send("Input.insertText", { text: USER });
  await sleep(250);
  await eval("window.__p.focus();return 1;");
  await send("Input.insertText", { text: PASS });
  await sleep(250);
  const vals = await eval("return JSON.stringify({u:window.__u.value,plen:window.__p.value.length});");
  console.log("typed vals:", vals);
  await eval("if(window.__p.blur)window.__p.blur();return 1;");
  await sleep(250);
  const clicked = await eval("const b=[...document.querySelectorAll('button')].find(b=>/login|로그인|sign\\\\s*in/i.test(b.textContent))||document.querySelector('button[type=submit]'); if(b){b.click();return b.textContent.trim();} return null;");
  console.log("login button:", clicked);
  await sleep(800);
  const err = await eval("const e=document.querySelector('.mud-alert,.validation-message,.mud-input-error'); return e?e.innerText.trim():null;");
  if (err) console.log("login error msg:", err);

  // 로그인 후 대시보드 렌더 대기
  await waitFor("document.querySelectorAll('nav a, aside a, .mud-nav-link').length>=4", 35000);
  await sleep(2500);
  console.log("post-login url:", await eval("return location.href"));

  // 다크 모드 활성화 — 상단 우측 아이콘 버튼 중 테마 토글 탐색(배경 명도 변화로 판별)
  const bgLight = await eval("return getComputedStyle(document.body).backgroundColor");
  const hbN = await eval("window.__hb=[...document.querySelectorAll('button')].filter(b=>{const r=b.getBoundingClientRect();return r.top<70 && r.left>window.innerWidth-380 && r.width>0 && r.width<70;}); return window.__hb.length;");
  let darkOn = false;
  for (let i = 0; i < hbN; i++) {
    await eval(`if(window.__hb[${i}])window.__hb[${i}].click(); return 1;`);
    await sleep(800);
    const bg = await eval("const m=(getComputedStyle(document.body).backgroundColor||'').match(/(\\d+),\\s*(\\d+),\\s*(\\d+)/); return m?Math.round(0.299*+m[1]+0.587*+m[2]+0.114*+m[3]):255;");
    if (bg < 110) { darkOn = true; console.log("dark toggled at btn", i, "lum", bg); break; }
    // 테마 토글이 아니었으면 열린 메뉴/패널 닫기
    await send("Input.dispatchKeyEvent", { type: "keyDown", key: "Escape", windowsVirtualKeyCode: 27 });
    await send("Input.dispatchKeyEvent", { type: "keyUp", key: "Escape", windowsVirtualKeyCode: 27 });
    await sleep(300);
  }
  console.log("dark mode:", darkOn, "| from", bgLight);
  await sleep(1000);
  await shot("41_web_dashboard.png");

  // 접힌 네비 그룹 모두 펼치기 (BUTTON aria-expanded=false)
  await eval("[...document.querySelectorAll('.mud-nav-link[aria-expanded=\\'false\\']')].forEach(g=>{try{g.click();}catch(e){}});return 1;");
  await sleep(1200);
  // 리프 메뉴(navigation) 텍스트 수집 — BUTTON(그룹)·외부링크(↗) 제외
  const leavesJson = await eval("return JSON.stringify([...document.querySelectorAll('.mud-nav-link')].filter(e=>e.tagName!=='BUTTON').map(e=>(e.textContent||'').replace(/\\s+/g,' ').trim()).filter(t=>t && !/↗/.test(t)));");
  const leaves = JSON.parse(leavesJson || "[]");
  console.log("LEAVES(" + leaves.length + "):", leavesJson);
  let idx = 42;
  for (const txt of leaves) {
    // 텍스트로 nav-link 클릭 (SPA 네비)
    const ok = await eval(`const els=[...document.querySelectorAll('.mud-nav-link')].filter(e=>e.tagName!=='BUTTON'); const el=els.find(e=>(e.textContent||'').replace(/\\s+/g,' ').trim()===${JSON.stringify(txt)}); if(el){el.scrollIntoView();el.click();return 1;} return 0;`);
    await sleep(2800);
    const slug = txt.replace(/[^0-9A-Za-z가-힣]+/g, "_").replace(/^_+|_+$/g, "").slice(0, 18) || "page";
    const fname = idx + "_web_" + slug + ".png"; idx++;
    await shot(fname);
    console.log("captured:", txt, "->", fname, "(click=" + ok + ")");
  }

  ws.close(); chrome.kill();
}
main().catch(e => { console.log("ERR", e.message); });
