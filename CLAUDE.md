# AnimeGo TV

Репозиторий: **AnimeGo-TV** (github.com/wepmaner/AnimeGo-TV), приложение на ТВ называется AnimeGoTV.

Приложение для Samsung TV, через которое можно смотреть аниме с animego.me без браузера.

## Зачем

Сейчас смотреть через браузер телевизора долго: нужно набрать адрес с пульта, часто заново войти в аккаунт, а браузер занимает много памяти и тормозит.
Приложение должно открываться сразу на «Продолжить просмотр».

## Что умеет (цель)

- Главный экран: «Продолжить просмотр» (начатые и недосмотренные серии), новинки, популярное — как на сайте.
- Поиск по каталогу animego.
- Учёт просмотренного **хранится на сайте**, у себя ничего не храним: читаем с animego, где остановились, и отправляем туда прогресс, чтобы на телефоне и ПК было то же самое.
- Вход в аккаунт animego.me.

## Стек (как в `C:\Users\Ruslan\Desktop\projects\domofon`)

- **Tizen .NET (C#)**, UI на ElmSharp, `Tizen.NET.Sdk/1.2.1`, `tizen40` (API 4 — см. грабли про Tizen 4.0), LangVersion 8.0.
- Почему не HTML/JS: веб-движок не даёт ставить свои `Cookie`/`Referer`/`User-Agent`, а для авторизации и видеоплееров они нужны. В .NET запросы полностью под нашим контролем, а видео (HLS) воспроизводит встроенный плеер ТВ.
- Устройство как в домофоне:
  - `src/AnimeGo.Core` — логика без UI (netstandard2.0, **без NuGet-пакетов**, JSON через `DataContractJsonSerializer`): запросы, разбор HTML/JSON, модели.
  - `src/AnimeGo.Tv` — приложение Tizen: экраны, плеер.
  - `tests/AnimeGo.Core.Tests` — xUnit на ПК, в `Fixtures/` лежат настоящие ответы сайта.
  - `scripts/deploy.sh` — сборка и установка на ТВ через `sdb`; `scripts/devlog.mjs` — приёмник отладочных строк.
- Разбор API сайта — `API-findings.md`.

## Телевизор

Samsung UE50TU7140 (2020, Tizen 5.5), `192.168.0.16`, режим разработчика, разрешён ПК `192.168.0.41`.
`sdb`: `~/tizen-tools/sdb/data/tools/sdb.exe`, в Git Bash нужен `MSYS_NO_PATHCONV=1`.

## Грабли из домофона (действуют и здесь)

- В Core нельзя тащить свою Newtonsoft.Json: на ТВ стоит своя старая версия, и они конфликтуют (`FileLoadException`).
- Подписки EFL (`EcoreEvent` и т.п.) хранить в полях, иначе их соберёт GC и приложение тихо упадёт.
- .NET на ТВ переписывает заголовки (порядок, `Host` первым). Если сайт или плеер к этому придирается, используем свой сырой HTTP.
- Логов на ТВ нет, поэтому Debug-сборка шлёт `DevLog` на ПК.
- Значок кешируется: при смене поднять `version` в `tizen-manifest.xml` и переустановить приложение.

## Грабли AnimeGo (проверено на ТВ)

- **Ссылки okcdn (CVH) подписаны под User-Agent** (`srcAg=CHROME` в адресе): с чужим UA ответ `400`, плеер ТВ пишет `ConnectionFailed` → `Failed to prepare the player`. Перед `SetSource` ставить `Player.UserAgent = SiteApi.UserAgent`. В адресе есть и `srcIp` (внешний IP запросившего), так что ссылку получает сам ТВ, а не ПК где-то в другой сети.
- **Постеры `img.cdngos.com` — только WebP** (на любой `Accept` и адрес), а EFL на ТВ WebP не открывает (`Image.Load` → false). Своим P/Invoke в `libwebp.so` пользоваться нельзя: ТВ отказывается запускать приложение («AnimeGo.Tv.dll refers a forbidden assembly directly - libwebp.so»), а `DevLog` молчит, потому что до `Main` дело не доходит. `WebPDecoder` в `Tizen.Multimedia.Util` (API 6) нет. Решение: `ImageCache` перегоняет WebP в PNG через SixLabors.ImageSharp 2.1.9 (чистый managed, только в `AnimeGo.Tv`). Он ссылается на `System.Runtime.CompilerServices.Unsafe` 5.0.0.0, а на ТВ своя, старее, и в общем контексте подменить её нельзя (`FileLoadException` 0x80131040), поэтому ImageSharp грузится в свой `AssemblyLoadContext` из папки приложения и вызывается только через отражение — прямая ссылка на его типы из кода сломает это. Декод+PNG постера 250×350 на ТВ ≈ 0,2–0,5 с (4 параллельно), первые после запуска ≈ 2–2,6 с (JIT); дальше берётся PNG из кеша. `.tpk` вырос со 115 КБ до 1,3 МБ. У ImageSharp 2.1.9 есть известные CVE (NU1902/NU1903) — 3.x требует net6, на ТВ его нет; картинки идут только с `img.cdngos.com`.
- Плеер (`Tizen.Multimedia.Player`, UE50TU7140) на CVH: HLS `auto` от `PlayAsync` до `Playing` ≈ 1,9 с, от нажатия ≈ 1,5–5,8 с (большая часть — запросы к animego/CVH); MP4 1080p ≈ 1,4 с от нажатия. HLS в фолбэк на MP4 не переводим.
- Перемотка (`SetPlayPositionAsync(ms, accurate:false)`) на HLS и MP4 занимает 0,1–0,9 с. Пока она идёт, новая падает с `SeekFailed`, поэтому `VideoView.Seek` копит нажатия и перематывает на последнюю цель, а ±10 с считаются от `TargetSec`. Удержание кнопки шлёт повтор `KeyDown` каждые 0,1–0,3 с.
- AniBoom (HLS, аудио отдельной дорожкой): доходит до `Playing` за 2,3 с, перемотка работает.
- Клавиш `FF`/`Rew`/`Stop`/`PlayPause` (`XF86AudioNext` и т.п.) на Samsung Smart Remote нет, их имена не проверены; подтверждены `XF86Back` и `XF86Exit`.
- Реальные имена клавиш пульта: `Up`, `Down`, `Left`, `Right`, `Return`, `Select`, `XF86Back`, `Cancel`, `XF86AudioPause`, `XF86AudioPlay`, `XF86AudioMute`, `XF86AudioLowerVolume`, `XF86Exit` (см. `Keys`). Громкость тоже приходит в приложение — её не перехватывать.
- OK пульта приходит парой `Return` + `Select` с разницей ≈ 8 мс: `App.OnKey` считает `Select` тем же OK и отбрасывает второе нажатие, если с первого прошло < 50 мс (время — монотонный `Clock.Ms`, не `Environment.TickCount`, который через ~24,9 суток уходит в минус). `Cancel` — «Назад» при открытой системной клавиатуре (иногда приходит `XF86Back`).
- В `DevLog` время с миллисекундами, шаги плеера меряются от нажатия клавиши (`[+N ms]`).
- **Системную клавиатуру ТВ (IME через `Entry`) приспособить не удалось** (UE50TU7140, раунды 6–9 задачи 14 — после исправления дедупа OK; в раундах до него OK глотался нашим же багом): «Готово» приходит тем же `Return`, что и выбор буквы; `Activated`/`Return` в `KeyDown` самого `Entry` срабатывают и на выбор буквы;
  смена языка/регистра — `Return` без изменения текста; «Назад» при открытом IME — `Cancel`. Отличить «Готово» не удалось, сигнала «IME скрыт» в ElmSharp API 6 не нашли.
  Ввод — своя QWERTY `OnScreenKeyboard`; `Keyboard.cs` (Entry) — запасной, включается `LoginScreen.UseNativeKeyboard = true`.
- **Значок приложения — в `src/AnimeGo.Tv/shared/res/animego.png`**: `<icon>` манифеста ищется в `shared/res/` пакета, из `res/` ТВ его не берёт (до 1.0.4 значка на ТВ не было). Копия в `res/` — для экрана входа.
- Значок рисуется из `design/logo/animego-icon.svg`, PNG 512×512 получен через `msedge --headless --screenshot` (других растеризаторов на ПК нет). Значок кешируется ТВ: после смены поднять `version`, `sdb uninstall ru.animego.tv` (стирает вход), `deploy.sh Release`.
- `dotnet build` ругается NU1902/NU1903 на `SixLabors.ImageSharp` 2.1.9 (NuGet, не C#); предупреждений компилятора 0.
- **Экраны не живут между переходами** (`App.Back` пересоздаёт их), поэтому разобранные ответы сайта лежат в `App.Cache`
  (`DataCache` в Core, память процесса, ≤120 записей) — см. `docs/superpowers/specs/2026-10-30-cache-skeletons-design.md`.
  Экран рисуется из кеша сразу, устаревшее перепроверяется в фоне; впервые — `Skeleton` (пульсирующие заглушки), не «Загрузка…».
  Общие запросы — `app.PlayerInfoAsync`/`AnimeInfoAsync`/`EpisodeVideosAsync`, а не `app.Api.*` напрямую. `Episode.Watched`
  плеер ставит на общих объектах, так что кеш `/player` после просмотра не врёт. Вход/выход/протухшие куки — `ForgetUserData`.
- **Собираемся под API 4 (`tizen40`, Tizen.NET 4.0.1.14164, `api-version="4"` в манифесте)**: у телевизоров 2018 года
  Tizen 4.0, и пакет с `api-version="6"` они отвергают (`processing result : Operation not allowed [-4] failed`, 2026-10-31,
  вывод там от `pkgcmd`, а не `wascmd`). Из нашего кода в API 4 нет только `Player.BufferingTime`/`PlayerBufferingTime`
  (API 5) — `VideoView.SetBufferingTime` ставит его через отражение. Под API 4 в пакет попадают `System.Memory`,
  `System.Buffers`, `System.Numerics.Vectors` (для ImageSharp; на Tizen 5.5 они встроенные). Запуск на Tizen 4.0 ещё не
  подтверждён: старый .NET там может не поднять ImageSharp (тогда без постеров, а не падение — `ImageCache` ловит).
- Кеш картинок (`DirectoryInfo.Cache`) чистится при старте в фоне: больше 120 МБ → удаляются давно не показывавшиеся до 90 МБ
  (время записи файла обновляется при показе). Версия приложения — в `tizen-manifest.xml` **и** `Stash.Version` (тест сверяет).
