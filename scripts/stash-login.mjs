#!/usr/bin/env node
// Проверка входа с ПК так, как это делает Stash: спрашивает логин и пароль (пароль не отображается и никуда
// не сохраняется), ждёт, пока на ТВ откроется экран входа (порт 8779 отвечает на /status), и шлёт POST /login.
//   node scripts/stash-login.mjs [ip-тв]        (по умолчанию 192.168.0.16)
// Запускать в своей консоли (PowerShell / cmd): для скрытого ввода пароля нужен настоящий терминал.
import { createInterface } from "node:readline";

const tv = process.argv[2] || "192.168.0.16";
const base = `http://${tv}:8779`;

function ask(q) {
  const rl = createInterface({ input: process.stdin, output: process.stdout });
  return new Promise((res) => rl.question(q, (a) => { rl.close(); res(a); }));
}

function askHidden(q) {
  return new Promise((resolve) => {
    if (!process.stdin.isTTY) { console.error("Нужна настоящая консоль: пароль вводится скрыто."); process.exit(2); }
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

async function status() {
  try {
    const r = await fetch(base + "/status", { signal: AbortSignal.timeout(1000) });
    if (r.status !== 200) return null;
    const j = await r.json();
    return j.screen === "login" ? j : null;
  } catch { return null; }
}

const login = (await ask("Логин animego: ")).trim();
const password = await askHidden("Пароль (не отображается): ");
if (!login || !password) { console.error("Нужны логин и пароль."); process.exit(2); }

console.log(`Жду экран входа на ${tv} (на ТВ: «Войти» вверху главной)… Ctrl+C — выход.`);
let st;
while (!(st = await status())) await new Promise((r) => setTimeout(r, 1000));
console.log(`${new Date().toLocaleTimeString()} порт 8779 открыт, приложение ${st.version}. Отправляю данные…`);

const t0 = Date.now();
const res = await fetch(base + "/login", {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ login, password }),
  signal: AbortSignal.timeout(30000),
});
const j = await res.json().catch(() => null);
const ms = Date.now() - t0;
if (j?.ok) console.log(`Вошли за ${ms} мс: ${j.user?.name} (id ${j.user?.id}).`);
else console.log(`Не вошли (HTTP ${res.status}, ${ms} мс): ${j?.error ?? "ответ не JSON"}`);

for (let i = 0; i < 5 && (await status()); i++) await new Promise((r) => setTimeout(r, 1000));
console.log((await status()) ? "Порт 8779 всё ещё открыт." : "Экран входа закрыт, порт 8779 закрыт.");
