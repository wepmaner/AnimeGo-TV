// Проверка входа на animego.me с ПК: повторяет SiteApi.LoginAsync и сохраняет страницу под аккаунтом.
// Запуск: node scripts/login-sample.mjs [--path "/profile/?type=all" --out mylist_all.html] [--dry-run]
// Пароль не печатается и не пишется на диск; значения кук не печатаются.
import { randomBytes } from "node:crypto";
import { mkdirSync, writeFileSync } from "node:fs";
import { dirname, join, basename } from "node:path";
import { fileURLToPath } from "node:url";
import { createInterface } from "node:readline";

const BASE = "https://animego.me";
// Те же значения, что в SiteApi.cs
const UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";
const ACCEPT = "text/html,*/*;q=0.8";
const LANG = "ru,en;q=0.8";

const args = process.argv.slice(2);
const flag = (n) => args.includes(n);
const opt = (n, d) => { const i = args.indexOf(n); return i >= 0 && args[i + 1] ? args[i + 1] : d; };
const dryRun = flag("--dry-run");
const pagePath = opt("--path", "/");
const outName = basename(opt("--out", "home_auth.html"));
const outDir = join(dirname(fileURLToPath(import.meta.url)), "..", "tests", "AnimeGo.Core.Tests", "Fixtures", "private");

function ask(q) {
  const rl = createInterface({ input: process.stdin, output: process.stdout });
  return new Promise((res) => rl.question(q, (a) => { rl.close(); res(a); }));
}

function askHidden(q) {
  return new Promise((resolve) => {
    if (!process.stdin.isTTY) { ask(q).then(resolve); return; } // не TTY: эхо не наш случай
    process.stdout.write(q);
    let buf = "";
    process.stdin.setRawMode(true);
    process.stdin.resume();
    process.stdin.setEncoding("utf8");
    const onData = (ch) => {
      for (const c of ch) {
        if (c === "\r" || c === "\n") {
          process.stdin.setRawMode(false); process.stdin.pause(); process.stdin.off("data", onData);
          process.stdout.write("\n"); resolve(buf); return;
        }
        if (c === "\u0003") { process.stdout.write("\n"); process.exit(130); }
        if (c === "\u007f" || c === "\b") buf = buf.slice(0, -1);
        else buf += c;
      }
    };
    process.stdin.on("data", onData);
  });
}

// Как Html.Decode в Core: именованные и числовые entities за один проход
const NAMED = { quot: '"', amp: "&", lt: "<", gt: ">", apos: "'", nbsp: " " };
function decodeEntities(s) {
  return s.replace(/&(#[xX][0-9a-fA-F]+|#[0-9]+|[A-Za-z][A-Za-z0-9]*);/g, (m, e) => {
    if (e[0] !== "#") return e in NAMED ? NAMED[e] : m;
    const cp = e[1] === "x" || e[1] === "X" ? parseInt(e.slice(2), 16) : parseInt(e.slice(1), 10);
    if (!(cp > 0 && cp <= 0x10ffff) || (cp >= 0xd800 && cp <= 0xdfff)) return m;
    return String.fromCodePoint(cp);
  });
}

// data-user на теге <html>: значение с entities (&#x7B;&quot;id&quot;...) -> декод -> JSON
function detectUser(html) {
  const tag = /<html\b[^>]*>/i.exec(html);
  if (!tag) return null;
  const m = /\sdata-user\s*=\s*(?:'([^']*)'|"([^"]*)")/i.exec(tag[0]);
  if (!m) return null;
  try { return JSON.parse(decodeEntities(m[1] ?? m[2])); } catch { return null; }
}

function describeUser(user, ok) {
  return ok ? "id=" + (user.id != null ? "<есть>" : "<нет>") + ", username=" + (user.username ? "<есть>" : "<нет>") : "нет";
}

if (flag("--self-test")) {
  const u = detectUser('<html lang="ru" data-user="&#x7B;&quot;id&quot;:7,&quot;username&quot;:&quot;x&quot;&#x7D;">');
  console.log("data-user: " + describeUser(u, !!u && Number(u.id) > 0));
  process.exit(u && Number(u.id) > 0 ? 0 : 1);
}

// Как Csrf.NewToken: 18 случайных байт, base64 (24 символа)
const token = randomBytes(18).toString("base64");
const csrfCookie = "__Host-csrf-token_" + token;

const jar = new Map(); // имя -> значение (значения не печатаем)
jar.set(csrfCookie, "csrf-token");
const cookieHeader = () => [...jar].map(([k, v]) => k + "=" + v).join("; ");

function formBody(login, password) {
  // Порядок и имена полей как в SiteApi.LoginAsync
  return new URLSearchParams([
    ["_username", login], ["_password", password], ["_remember_me", "on"], ["_csrf_token", token],
  ]).toString();
}

function headers(extra) {
  return { "User-Agent": UA, "Accept-Language": LANG, Accept: ACCEPT, Cookie: cookieHeader(), ...extra };
}

function absorb(res) {
  for (const sc of res.headers.getSetCookie()) {
    const parts = sc.split(";").map((s) => s.trim());
    const eq = parts[0].indexOf("=");
    if (eq <= 0) continue;
    const name = parts[0].slice(0, eq), value = parts[0].slice(eq + 1);
    const attrs = Object.fromEntries(parts.slice(1).map((p) => { const i = p.indexOf("="); return i < 0 ? [p.toLowerCase(), ""] : [p.slice(0, i).toLowerCase(), p.slice(i + 1)]; }));
    const expired = ("max-age" in attrs && Number(attrs["max-age"]) <= 0) || ("expires" in attrs && Date.parse(attrs.expires) < Date.now());
    if (expired || value === "") jar.delete(name); else jar.set(name, value);
  }
}

if (dryRun) {
  console.log("[dry-run] сеть не используется");
  console.log("POST " + BASE + "/login   (редиректы не следуем)");
  const h = headers({ "Content-Type": "application/x-www-form-urlencoded" });
  for (const [k, v] of Object.entries(h)) console.log("  " + k + ": " + (k === "Cookie" ? "<значения скрыты>" : v));
  console.log("csrf-кука: " + csrfCookie + "=csrf-token");
  console.log("тело: " + formBody("<логин>", "***").replace("%3C%D0%BB%D0%BE%D0%B3%D0%B8%D0%BD%3E", "<логин>"));
  console.log("затем GET " + BASE + pagePath + " с куками, сохранить в Fixtures/private/" + outName);
  process.exit(0);
}

const login = await ask("Логин: ");
const password = await askHidden("Пароль (не отображается): ");

const post = await fetch(BASE + "/login", {
  method: "POST", redirect: "manual",
  headers: headers({ "Content-Type": "application/x-www-form-urlencoded" }),
  body: formBody(login, password),
});
absorb(post);
console.log("POST /login -> " + post.status);
const loc = post.headers.get("location");
if (loc) console.log("Location: " + loc);

const page = await fetch(BASE + pagePath, { redirect: "manual", headers: headers({}) });
absorb(page);
console.log("GET " + pagePath + " -> " + page.status);
console.log("Куки (только имена): " + [...jar.keys()].join(", "));

let html = await page.text();

const user = detectUser(html);
const ok = !!user && Number(user.id) > 0;
console.log("data-user: " + describeUser(user, ok));

html = html.replace(/data-csrf-token="[^"]*"/g, 'data-csrf-token="X"');
mkdirSync(outDir, { recursive: true });
writeFileSync(join(outDir, outName), html, "utf8");
console.log("Сохранено: tests/AnimeGo.Core.Tests/Fixtures/private/" + outName);

if (!ok) {
  console.log("Вход не подтверждён. Проверьте: верны ли логин и пароль; код POST /login (ожидали 302) и Location (/ — успех, /login — отказ);");
  console.log("не изменилась ли схема CSRF (см. API-findings.md, «Вход»); при необходимости попробуйте добавить Referer/Origin: " + BASE);
  process.exit(1);
}
