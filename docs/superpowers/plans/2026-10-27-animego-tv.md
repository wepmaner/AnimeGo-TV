# AnimeGo TV — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Приложение для Samsung TV (Tizen .NET), которое открывается на «Продолжить просмотр» animego.me и воспроизводит серии своим плеером, храня историю на сайте.

**Architecture:** Как в проекте домофона: `AnimeGo.Core` (netstandard2.0, без NuGet) — запросы к сайту, разбор HTML/JSON, чистые модели (озвучки, «давно не смотрели», отправка прогресса) с xUnit-тестами на настоящих ответах сайта; `AnimeGo.Tv` (Tizen .NET, ElmSharp, абсолютная раскладка 1920×1080) — экраны и плеер `Tizen.Multimedia.Player`. Видео берётся прямыми ссылками HLS/MP4 с CDN (CVH → AniBoom), плееру ТВ отдаётся URL напрямую.

**Tech Stack:** C# 8, .NET (netstandard2.0 Core, `Tizen.NET.Sdk/1.2.1` / `tizen60` Tv, net10.0 тесты), xUnit, ElmSharp, Tizen.Multimedia, `sdb`, Node для отладочного приёмника.

**Spec:** `docs/superpowers/specs/2026-10-27-animego-tv-design.md` (+ `API-findings.md`, `CLAUDE.md`, макеты `design/mockups/`).

## Global Constraints

- Core: `netstandard2.0`, `LangVersion 8.0`, **никаких NuGet-пакетов**; JSON только `DataContractJsonSerializer`.
- Tv: `Tizen.NET.Sdk/1.2.1`, `TargetFramework tizen60`, `TizenNetPackageVersion 6.0.0.14995`, `LangVersion 8.0`; подписки EFL (`EcoreEvent`) — только в полях.
- Запросы к сайту: `User-Agent` браузера (`Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36`), для AJAX-URL `X-Requested-With: XMLHttpRequest` и `Accept: application/json`; `plapi.cdnvideohub.com` — с `Referer: https://animego.me/`; `aniboom.one` — с `Referer: https://animego.me/`.
- Все парсеры тестируются на файлах из `research/samples/` (копируются в `tests/AnimeGo.Core.Tests/Fixtures/`).
- Цвета/размеры — из макетов: фон `#0e0f13`, карточка `#1b1d24` / `#22252d`, текст `#f2f3f5`, приглушённый `#b4b9c3`, акцент `#e5484d`, фокус — белая рамка 4 px с зазором 4 px. Экран 1920×1080, поля 80 px.
- Порядок источников видео: `cvh` → `aniboom`; `kodik` не воспроизводится.
- Прогресс: `POST /video/progress` JSON `{episodeId, translationId, animeId, currentTime, duration}` (целые секунды), не чаще чем при изменении позиции ≥ 5 с, плюс при паузе, перемотке, окончании, выходе. «Просмотрено»: при ≥ 80 % — сначала `GET /player/videos/{episodeId}`, POST `/anime/episode/watched` (`id`, `translationId`) только если `isWatched != true`, один раз за серию.
- «Давно не смотрели»: 14 дней без изменения карточки; локальные данные ключуются `id` пользователя (`data-user`) или `guest`.
- Коммиты после каждой задачи; сообщения коммитов на русском, в теле — `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.

## Review Focus

1. Карточка «Продолжить просмотр» без озвучки в локальной памяти и с серией, которой уже нет у основной студии, — приложение должно открыть панель выбора, а не упасть и не включить чужую озвучку молча (Task 10, 17).
2. Сайт вернул HTML вместо JSON (сессия протухла, DDoS-Guard, 5xx) — парсер бросает `ApiException`, экран показывает «Нет связи» с «Повторить», а не пустой ряд без объяснений (Task 7, 14).
3. Ссылка CVH протухла или `okcdn` ответил не 200 — плеер переходит к следующему источнику/озвучке, а не висит на чёрном экране (Task 16).
4. Отметка «просмотрено» не должна сниматься: при повторном просмотре уже отмеченной серии POST не уходит; при ≥ 80 % POST уходит ровно один раз (Task 11, 7).
5. Выход из плеера «Назад» на 3-й секунде: позиция отправляется один раз с `currentTime`, а не 0, и не отправляется, если серия не стартовала (Task 11, 16).

---

## Структура файлов

```
AnimeGo.sln
src/AnimeGo.Core/
  AnimeGo.Core.csproj
  Html.cs            — мини-разбор HTML регулярками: атрибуты, текст, entity-декод, блоки по классу
  Json.cs            — Json.Parse<T>/Serialize, DTO обёртки сайта (AjaxDto) и CVH
  Models.cs          — публичные модели: AnimeCard, ContinueCard, Episode, Translation, VideoSource, Stream, User
  ApiException.cs    — ApiErrorKind {Network, Server, Unauthorized, Parse}
  CatalogParser.cs   — карточки .ani-list__item / .ani-grid__item, параметры пагинации
  HomeParser.cs      — data-user, «Продолжить просмотр», секция сезона
  PlayerParser.cs    — /player/{id} и /player/videos/{id}: серии, озвучки, источники, isWatched
  AnimeParser.cs     — страница тайтла: название, описание, жанры, постер, «Связанное»
  CvhClient.cs       — плейлист и видео cdnvideohub, выбор элемента и качества
  AniboomParser.cs   — data-parameters → hls/dash
  Csrf.cs            — токен и кука по схеме сайта
  CookieJar.cs       — куки сайта + сериализация в строку
  SiteApi.cs         — HttpClient с куками, все запросы к animego.me и CDN
  Prefs.cs           — локальный JSON: пользователь → озвучки, seen; качество
  ContinueModel.cs   — разделение на «Продолжить» / «Давно не смотрели», обновление seen
  DubChooser.cs      — основная → запасная → спросить
  ProgressTracker.cs — когда слать прогресс; WatchedTracker — порог 80 %
  Seasons.cs         — сезоны из «Связанного» + текущего тайтла
  RuText.cs          — все строки интерфейса
tests/AnimeGo.Core.Tests/   (xUnit, Fixture.cs, FakeHandler.cs как в домофоне, Fixtures/)
src/AnimeGo.Tv/
  AnimeGo.Tv.csproj, tizen-manifest.xml (package ru.animego.tv), res/
  Program.cs   — App: окно, клавиши, навигация, Ui(), SiteApi, Prefs
  Theme.cs, Widgets.cs, DevLog.cs — как в домофоне (цвета новые)
  ImageCache.cs — загрузка постеров в файл-кеш + установка в Image
  Keyboard.cs  — поле ввода с экранной клавиатурой ТВ (ElmSharp Entry)
  LoginScreen.cs, HomeScreen.cs, GridScreen.cs, TitleScreen.cs
  PlayerScreen.cs — видео, панель, серии; ChoicePanel.cs — озвучка/качество; VideoView.cs
scripts/deploy.sh (TV=ip:port параметром), scripts/devlog.mjs, scripts/login-sample.mjs
```

---

### Task 1: Каркас репозитория и проектов

**Files:**
- Create: `AnimeGo.sln`, `src/AnimeGo.Core/AnimeGo.Core.csproj`, `src/AnimeGo.Core/ApiException.cs`, `src/AnimeGo.Core/RuText.cs`, `tests/AnimeGo.Core.Tests/AnimeGo.Core.Tests.csproj`, `tests/AnimeGo.Core.Tests/Fixture.cs`, `tests/AnimeGo.Core.Tests/FakeHandler.cs`, `tests/AnimeGo.Core.Tests/RuTextTests.cs`, `src/AnimeGo.Tv/AnimeGo.Tv.csproj`, `src/AnimeGo.Tv/tizen-manifest.xml`, `src/AnimeGo.Tv/Program.cs`, `scripts/deploy.sh`, `scripts/devlog.mjs`, `.gitignore`, `README.md`
- Copy: `research/samples/*` → `tests/AnimeGo.Core.Tests/Fixtures/` (все файлы)

**Interfaces:**
- Produces: `public enum ApiErrorKind { Network, Server, Unauthorized, Parse }`, `public sealed class ApiException : Exception { public ApiErrorKind Kind; ApiException(ApiErrorKind kind, string message, Exception inner = null) }`; `public static class RuText` с константами (см. шаг 3); `Fixture.Read(name)`, `FakeHandler` (копия из домофона с заменой namespace).

- [ ] **Step 1: `git init`, `.gitignore`** (`bin/ obj/ .superpowers/ *.user research/samples/*.html` → нет: samples нужны в репо, игнорируем только `bin obj .superpowers .vs`), первый коммит с текущими документами.
- [ ] **Step 2: Создать проекты** по образцу `C:\Users\Ruslan\Desktop\projects\domofon` (csproj Core/Tests/Tv скопировать, заменив имена; из Tv-csproj убрать цель `CopyToken`). `tizen-manifest.xml`: package/appid `ru.animego.tv`, label `AnimeGo`, privilege `internet`, icon временно — любой PNG 512×512 в `res/`.
- [ ] **Step 3: `RuText`** — константы: `Continue = "Продолжить просмотр"`, `Stale = "Давно не смотрели"`, `StaleHint = "· больше 14 дней"`, `Genres = "Жанры"`, `NewEpisodes = "Новые серии"`, `Login = "Войти"`, `LoginWithout = "Продолжить без входа"`, `NoConnection = "Нет связи с animego.me"`, `Retry = "Повторить"`, `Dub = "Озвучка"`, `Quality = "Качество"`, `NextEpisode = "Следующая серия"`, `SkipOpening = "Пропустить опенинг"`, `MakeMain = "Сделать основной"`, `Main = "основная"`, `Fallback = "запасная"`, `NewEpisodeBadge = "Новая серия"`, `BadLogin = "Неверный логин или пароль"`, `SourceFailed = "Не удалось включить видео в этой озвучке"`, `Unavailable = "недоступно в приложении"`, `SwitchedSource = "Переключил источник"`, `Loading = "Загрузка…"`; функции `Episode(int n) => "Серия " + n`, `Time(int s)` → `"05:08"` / `"1:05:08"` при ≥ 1 ч, `Remaining(int s) => "−" + Time(s)`, `NoDub(string studio, int ep) => studio + " серию " + ep + " пока не озвучила"`, `SwitchedDub(string from, int ep, string to)`.
- [ ] **Step 4: Тест** `RuTextTests`: `Time(308) == "05:08"`, `Time(3908) == "1:05:08"`, `Episode(22) == "Серия 22"`.
- [ ] **Step 5: Запустить** `dotnet test` — PASS; `dotnet build src/AnimeGo.Tv -c Release` — собирается `.tpk`.
- [ ] **Step 6: `scripts/deploy.sh`** — копия из домофона, но `TV=${TV:-192.168.0.16:26101}` и appid `ru.animego.tv`; `scripts/devlog.mjs` — копия. `README.md` — как у домофона, кратко.
- [ ] **Step 7: Commit** `chore: каркас проектов AnimeGo`.

---

### Task 2: Html и Json — инструменты разбора

**Files:**
- Create: `src/AnimeGo.Core/Html.cs`, `src/AnimeGo.Core/Json.cs`
- Test: `tests/AnimeGo.Core.Tests/HtmlTests.cs`, `JsonTests.cs`

**Interfaces:**
- Produces (`internal static class Html`):
  - `string Attr(string tag, string name)` — значение атрибута из текста одного тега (кавычки `"`/`'`, entity-декод), `null` если нет.
  - `IEnumerable<string> Tags(string html, string tagName, string classToken)` — тексты открывающих тегов `<a ...>` с классом (по токену в `class`).
  - `IEnumerable<string> Blocks(string html, string tagName, string classToken)` — целые элементы (от `<div class="x">` до парного `</div>` с учётом вложенности того же тега).
  - `string Text(string html)` — убрать теги, схлопнуть пробелы, декодировать entities (`&quot; &amp; &lt; &gt; &#x5B; &#39; &nbsp;` и числовые).
  - `string Decode(string s)` — только entity-декод.
- Produces (`internal static class Json`): `T Parse<T>(string)`, `string Serialize<T>(T)`; `[DataContract] AjaxDto { status, message, data: AjaxDataDto { content, numVideos, isWatched (bool?) } }`.

- [ ] **Step 1: Тесты Html** — `Attr("<a href=\"/x\" data-ep='5'>", "data-ep") == "5"`; `Decode("&#x5B;&quot;cvh&quot;&#x5D;") == "[\"cvh\"]"`; `Blocks` на `<div class="a"><div class="b">1</div></div><div class="a">2</div>` даёт 2 блока; `Text("<b>Серия</b>  22") == "Серия 22"`.
- [ ] **Step 2: Тест Json** — `Parse<AjaxDto>(Fixture.Read("player_videos_44100.json")).Data.NumVideos == 25` и `IsWatched == null`.
- [ ] **Step 3: Запустить** — FAIL (типов нет).
- [ ] **Step 4: Реализовать** `Html` (регулярки с `RegexOptions.Singleline`, без рекурсии глубже стека), `Json` как в домофоне + `Serialize`.
- [ ] **Step 5: Запустить** — PASS. **Step 6: Commit** `feat(core): Html и Json`.

---

### Task 3: CatalogParser — карточки и пагинация

**Files:**
- Create: `src/AnimeGo.Core/Models.cs` (часть: `AnimeCard`, `Paging`), `src/AnimeGo.Core/CatalogParser.cs`
- Test: `tests/AnimeGo.Core.Tests/CatalogParserTests.cs`

**Interfaces:**
- Produces: `public sealed class AnimeCard { int Id; string Slug; string Title; string TitleOriginal; string PosterUrl; string Rating; string Kind; IReadOnlyList<string> Genres; string Url => "/anime/" + Slug + "-" + Id; }`;
  `public sealed class Paging { string UrlTemplate; int Page; int PageSize; int TotalItems; bool HasMore; string Params; string PageUrl(int n) => UrlTemplate.Replace("__page__", n) + "?" + Params-as-query }`;
  `public static class CatalogParser { IReadOnlyList<AnimeCard> Cards(string html); Paging Paging(string html); IReadOnlyList<AnimeCard> CardsFromAjax(string json) }` — `CardsFromAjax` = `Cards(Json.Parse<AjaxDto>(json).Data.Content)`.
- Разметка: список `.ani-list__item` (каталог/поиск: `a.ani-list__item-picture[href]`, `img.image__img[src][alt]`, `.ani-list__item-title a[title]`, `.rating-badge`, `.ani-list__item-genres a`), сетка `.ani-grid__item` (главная: `a.ani-grid__item-picture[href]`, `img[src][alt]`, `.ani-grid__item-title a[title]`); id и slug — из `href` регуляркой `^/anime/(.+)-(\d+)$`. Постер в сетке запрашивать как есть; для 140×196 на ТВ размер в URL менять на `/v/250x350/`.

- [ ] **Step 1: Тесты** — `Cards(Fixture catalog_ongoing_page1.html)`: 20 карточек, первая `Id == 4149`, `Title == "Легенда о Белом Коте 3"`, `PosterUrl` начинается с `https://img.cdngos.com/v/250x350/`; `Paging(...)`: `UrlTemplate == "/anime/status/ongoing/__page__"`, `PageSize == 20`, `TotalItems == 81`, `HasMore`, `PageUrl(2) == "/anime/status/ongoing/2?entities=true"`; `CardsFromAjax(catalog_ongoing_page2.json)` — 20 карточек; `Cards(search_okean.html)` содержит `Id 3547`, `2817`, `645`; `Cards(home_anon.html)` (сетка сезона) содержит `Id 3815` с `Title "Свадьба светлячков"`.
- [ ] **Step 2: FAIL. Step 3: Реализовать.** `Params` разбирать из `data-infinite-scroll-params-value` (entity-декод + `Json.Parse<Dictionary<string,string>>` не поддерживается DataContract — разобрать регуляркой `"(\w+)":"(\w+)"`).
- [ ] **Step 4: PASS. Step 5: Commit** `feat(core): разбор карточек каталога и пагинации`.

---

### Task 4: PlayerParser — серии, озвучки, источники

**Files:**
- Modify: `src/AnimeGo.Core/Models.cs` (+ `Episode`, `Translation`, `VideoSource`, `PlayerInfo`, `EpisodeVideos`)
- Create: `src/AnimeGo.Core/PlayerParser.cs`
- Test: `tests/AnimeGo.Core.Tests/PlayerParserTests.cs`

**Interfaces:**
- Produces: `public sealed class Episode { int Id; int Number; bool Watched; int Page; }`; `public sealed class Translation { string Key; int? Id; string Title; string Kind /* "Дубляж"/"Многоголосый"/"Субтитры"/"" из текста, если есть, иначе "" */ }`; `public sealed class VideoSource { string Provider /* cvh|aniboom|kodik */; string PlayerUrl /* с https: */; string TranslationKey; int? TranslationId; string TranslationTitle; }`;
  `public sealed class PlayerInfo { int AnimeId; IReadOnlyList<Episode> Episodes; IReadOnlyList<Translation> Translations; IReadOnlyList<VideoSource> Sources; int EpisodesTotal; IReadOnlyList<string> ProviderPriority; string ContinuationUrl; string EpisodesUrl; }`;
  `public sealed class EpisodeVideos { bool? IsWatched; int NumVideos; IReadOnlyList<VideoSource> Sources; }`;
  `public static class PlayerParser { PlayerInfo Parse(string json); EpisodeVideos ParseVideos(string json); }`.
- Разметка: корень `.player-video` с `data-anime-player-*-value`; серии `[data-episode][data-episode-number][data-episode-watched]`; озвучки `[data-anime-player-target="translation"]` (`data-translation`, `data-translation-id`, текст без «(ошибка)»); источники `button[data-anime-player-target="provider"]` (`data-player`, `data-provider-slug`, `data-ptranslation`, `data-translation-id`, `data-translation-title`). `PlayerUrl`: `//x` → `https://x`, `&amp;` → `&`.

- [ ] **Step 1: Тесты** на `player_3547.json`: `AnimeId == 3547`, `Episodes.Count == 12`, `Episodes[0].Id == 44100 && Number == 1 && !Watched`, `Translations` содержит `Key "59"` с `Title "JAM CLUB"`, `Sources.Count == 25`, источник `cvh` для `TranslationKey "59"` имеет `PlayerUrl == "https://animego.me/cdn-iframe/62542/Jam Club/3/1"`, `ProviderPriority == ["cvh","aniboom"]`, `ContinuationUrl == "/player/3547/continuation/1"`. На `player_videos_44100.json`: `IsWatched == null`, `NumVideos == 25`, `Sources.Count == 25`.
- [ ] **Step 2: FAIL. Step 3: Реализовать. Step 4: PASS. Step 5: Commit** `feat(core): разбор плеера`.

---

### Task 5: AnimeParser — страница тайтла и «Связанное»

**Files:**
- Modify: `Models.cs` (+ `AnimeInfo`, `RelatedTitle`)
- Create: `src/AnimeGo.Core/AnimeParser.cs`; Test: `AnimeParserTests.cs`

**Interfaces:**
- Produces: `public sealed class RelatedTitle { int Id; string Slug; string Title; string Kind; string Year; string PosterUrl }`; `public sealed class AnimeInfo { int Id; string Title; string TitleOriginal; string Description; string PosterUrl; IReadOnlyList<string> Genres; string Kind; string Year; IReadOnlyList<RelatedTitle> Related; }`; `public static class AnimeParser { AnimeInfo Parse(string html, int id) }`.
- Разметка: `<h1>`, `.entity__title-synonyms`, `.description` (текст), постер — `img` внутри первого `[data-fancybox]`/`.anime-poster` (уточнить по фикстуре), жанры — ссылки `href="/anime/genre/…"`, «Связанное» — блоки `.seasons-item` (`a.seasons__link[href]` + текст, `img.seasons__img[src]`, в `.seasons__info` первые два `<span>` — тип и год).

- [ ] **Step 1: Тесты** на `anime_3547.html`: `Title == "Необъятный океан 3"`, `Description.Length > 100`, `Genres` содержит `"Комедия"`, `Related` содержит `Id 2817` с `Title "Необъятный океан 2"` и `Year "2025"`, `PosterUrl` начинается с `https://img.cdngos.com/`.
- [ ] **Step 2–5:** FAIL → реализовать → PASS → Commit `feat(core): разбор страницы тайтла`.

---

### Task 6: CVH и AniBoom — ссылки на видео

**Files:**
- Modify: `Json.cs` (+ `CvhPlaylistDto { titleName, isSerial, items[] { cvhId, vkId, voiceStudio, voiceType, season, episode } }`, `CvhVideoDto { duration, sources { hlsUrl, dashUrl, mpegFullHdUrl, mpegHighUrl, mpegMediumUrl, mpegLowUrl, mpegLowestUrl } }`), `Models.cs` (+ `Stream`, `StreamSet`)
- Create: `src/AnimeGo.Core/CvhClient.cs` (чистая часть), `src/AnimeGo.Core/AniboomParser.cs`
- Test: `CvhTests.cs`, `AniboomParserTests.cs`

**Interfaces:**
- Produces: `public sealed class Stream { string Url; string Quality /* "auto"|"1080p"|"720p"|"480p"|"360p"|"240p" */; bool IsHls; }`; `public sealed class StreamSet { IReadOnlyList<Stream> Streams; int DurationSec; Stream Pick(string preferredQuality) /* точное; иначе ближайшее ниже; иначе auto */ }`;
  `public static class Cvh { (int titleId, string voice, int season, int episode)? ParseIframeUrl(string playerUrl); string PlaylistUrl(int titleId) /* https://plapi.cdnvideohub.com/api/v1/player/sv/playlist?pub=747&aggr=mali&id= */; string VideoUrl(string vkId); CvhPlaylistDto ParsePlaylist(string json); CvhItemDto FindItem(CvhPlaylistDto p, string voice, int season, int episode) /* voice сравнивать без регистра и пробелов */; StreamSet ParseVideo(string json) /* hls → auto; mpegFullHd → 1080p, High → 720p, Medium → 480p, Low → 360p, Lowest → 240p; пустые пропускать */; }`;
  `public static class AniboomParser { StreamSet Parse(string embedHtml) /* data-parameters → hls.src (auto, IsHls), dash пропускаем */; string EmbedUrl(string playerUrl) }`.

- [ ] **Step 1: Тесты** — `Cvh.ParseIframeUrl("https://animego.me/cdn-iframe/62542/Jam Club/3/1") == (62542,"Jam Club",3,1)`; `ParsePlaylist(cvh_playlist_62542.json).Items.Count > 20`; `FindItem(p, "Студийная Банда", 3, 1).VkId == "15266286492336"`; `ParseVideo(cvh_video_redacted.json)`: `DurationSec == 1440`, есть `auto` (IsHls) и `1080p`, `Pick("720p").Quality == "720p"`, `Pick("4k").Quality == "auto"`; `AniboomParser.Parse(aniboom_embed.html).Streams[0].Url == "https://sophia.ya-ligh123.site/mk/mkPdyoERMOQ/master.m3u8"`, `DurationSec == 1439`.
- [ ] **Step 2–5:** FAIL → реализовать → PASS → Commit `feat(core): источники CVH и AniBoom`.

---

### Task 7: Csrf, CookieJar и SiteApi

**Files:**
- Create: `src/AnimeGo.Core/Csrf.cs`, `CookieJar.cs`, `SiteApi.cs`
- Test: `CsrfTests.cs`, `CookieJarTests.cs`, `SiteApiTests.cs`

**Interfaces:**
- Produces: `public static class Csrf { string NewToken() /* base64 от 18 случайных байт */; string CookieName(string token) => "__Host-csrf-token_" + token; }`;
  `public sealed class CookieJar { CookieContainer Container; string Export(); static CookieJar Import(string); bool IsEmpty; void Clear(); }` (экспорт — строки `name\tvalue\tdomain\tpath\texpiresUtcTicks`);
  `public sealed class SiteApi { SiteApi(CookieJar jar, HttpMessageHandler handler = null); const string Base = "https://animego.me"; Task<string> GetHtmlAsync(string path); Task<string> GetAjaxAsync(string path) /* заголовки AJAX; если Content-Type не json → ApiException(Parse) */; Task<User> LoginAsync(string login, string password) /* см. ниже */; Task<User> WhoAmIAsync() /* GET /, data-user → User или null */; Task LogoutAsync() /* jar.Clear() */; Task<IReadOnlyList<AnimeCard>> CatalogPageAsync(Paging p, int n); Task<IReadOnlyList<AnimeCard>> SearchAsync(string q); Task<PlayerInfo> PlayerAsync(int animeId); Task<EpisodeVideos> EpisodeVideosAsync(int episodeId); Task PostProgressAsync(int episodeId, int translationId, int animeId, int currentTime, int duration); Task PostWatchedAsync(int episodeId, string translationKey); Task<StreamSet> CvhStreamsAsync(VideoSource s, int season, int episode); Task<StreamSet> AniboomStreamsAsync(VideoSource s); }`; `public sealed class User { int Id; string Name; }`.
- `LoginAsync`: `token = Csrf.NewToken()`; добавить в jar куку `Csrf.CookieName(token)=csrf-token` для `animego.me` path `/` Secure; `POST /login` form `_username, _password, _remember_me=on, _csrf_token=token`, редиректы **не** следовать (`AllowAutoRedirect=false`); затем `WhoAmIAsync()`; `null` → `ApiException(Unauthorized, RuText.BadLogin)`.
- Handler по умолчанию: `HttpClientHandler { CookieContainer = jar.Container, UseCookies = true, AllowAutoRedirect = false, AutomaticDecompression = GZip|Deflate }`; таймаут 15 с; `User-Agent` из Global Constraints.
- Ошибки: сеть → `Network`; 401/403 → `Unauthorized`; иные не-2xx/3xx → `Server`; не-JSON там, где ждём JSON → `Parse`.

- [ ] **Step 1: Тесты** с `FakeHandler`: `LoginAsync` шлёт `POST /login` с телом, содержащим `_username=u&_password=p&_remember_me=on&_csrf_token=<32+ символов>` и заголовком `Cookie` с `__Host-csrf-token_<token>=csrf-token`; после ответа 302 и затем `GET /` с `<html data-user='{"id":7,"username":"x"}'>` возвращает `User{7,"x"}`; при `GET /` без `data-user` — `ApiException.Kind == Unauthorized`. `GetAjaxAsync` на ответ `text/html` → `Parse`. `PostProgressAsync(44100,59,3547,123,1440)` шлёт JSON ровно `{"episodeId":44100,"translationId":59,"animeId":3547,"currentTime":123,"duration":1440}` с `Content-Type: application/json`. `PostWatchedAsync(44100,"59")` шлёт `id=44100&translationId=59` с `X-Requested-With`. `CookieJar.Import(Export())` сохраняет куку с доменом и сроком. `Csrf.NewToken()` соответствует `^[-_/+a-zA-Z0-9]{24,}$`.
- [ ] **Step 2–5:** FAIL → реализовать → PASS → Commit `feat(core): SiteApi, вход и куки`.

---

### Task 8: Проверка входа с ПК и фикстура главной с входом

**Files:**
- Create: `scripts/login-sample.mjs`

Цель: проверить схему CSRF/входа на настоящем сервере до ТВ (риск §11.2 спеки) и получить `home_auth.html` для Task 9. Скрипт запускает **пользователь** (пароль не попадает в репозиторий и в чат).

- [ ] **Step 1: Написать скрипт** (Node 24, без зависимостей): спрашивает логин и пароль через `readline` (пароль без эха), делает ровно то же, что `SiteApi.LoginAsync` (токен, кука `__Host-csrf-token_T=csrf-token`, `POST /login`, куки из `set-cookie` сохраняет вручную), затем `GET /` с куками, и: печатает статус `/login`, список имён кук (без значений), `data-user`; сохраняет `GET /` в `tests/AnimeGo.Core.Tests/Fixtures/private/home_auth.html` (папка `private/` в `.gitignore`: страница содержит логин, id и историю просмотра — **в репозиторий она не попадает никогда**), предварительно заменив значение `data-csrf-token="…"` на `data-csrf-token="X"`. Выход 0 при `data-user`, 1 иначе. Параметры `--path "/profile/?type=all" --out mylist_all.html` сохраняют другую страницу в ту же папку.
- [ ] **Step 2: Попросить пользователя запустить** `! node scripts/login-sample.mjs` и прислать вывод. Если `data-user` нет — разобраться (сравнить с `API-findings.md`: возможно, нужны `Referer`/`Origin: https://animego.me`, или сервер ждёт `__Host-` только на https — мы и так на https), поправить и скрипт, и `SiteApi` одинаково; записать результат (имена кук, код ответа) в `API-findings.md`.
- [ ] **Step 3: Commit** `chore: скрипт проверки входа и фикстура главной с входом`.

---

### Task 9: HomeParser — «Продолжить просмотр», сезон, пользователь

**Files:**
- Modify: `Models.cs` (+ `ContinueCard`, `HomePage`)
- Create: `src/AnimeGo.Core/HomeParser.cs`; Test: `HomeParserTests.cs`

**Interfaces:**
- Produces: `public sealed class ContinueCard { int AnimeId; string Slug; string Title; int EpisodeNumber; int PositionSec; int DurationSec; string FrameUrl; double Progress => Position/Duration; }`; `public sealed class HomePage { User User; IReadOnlyList<ContinueCard> Continue; string SeasonTitle /* "Аниме осеннего сезона" */; string SeasonUrl /* /anime/season/2026/fall */; IReadOnlyList<AnimeCard> Season; }`; `public static class HomeParser { HomePage Parse(string html); User ParseUser(string html); }`.
- Разметка: `data-user` на `<html>` (JSON с `id`, `username`; entity-декод); `.continue-watching-section` → блоки `a.cw__link` (`href` → slug/id, `img.cw__img[src][alt]`, текст карточки `Серия N`, `mm:ss / mm:ss`, название — по порядку текстовых узлов; `data-anime-id` у кнопки удаления — запасной способ взять id); секция сезона — `.season-section` (`a.homepage-header__link[href]` + текст заголовка, сетка `.ani-grid__item` через `CatalogParser.Cards` на этом блоке).

- [ ] **Step 1: Тесты.** Приватная фикстура `Fixtures/private/home_auth.html` (не в git): тесты на ней помечены `[SkippableFact]` (пакет `Xunit.SkippableFact` — в тестовом проекте NuGet разрешён) и пропускаются с сообщением «нет private/home_auth.html», если файла нет; `Fixture.ReadPrivate(name)` возвращает `null` при отсутствии. Проверки: `User != null && User.Id > 0 && !string.IsNullOrEmpty(User.Name)`, `Continue.Count >= 1`, у каждой карточки `AnimeId > 0`, `EpisodeNumber >= 1`, `0 <= PositionSec <= DurationSec`, `DurationSec > 0`, `FrameUrl` начинается с `https://img.cdngos.com/episode/`, `Title` непустой. Публичная фикстура `home_anon.html`: `User == null`, `Continue.Count == 0`, `SeasonUrl == "/anime/season/2026/fall"`, `Season.Count >= 8`. Плюс синтетический тест на одну карточку (HTML-фрагмент, собранный в тесте из известной структуры `a.cw__link` с текстами `Серия 22`, `00:14 / 24:04`, названием и `style="width: 0.97%"`): `EpisodeNumber == 22`, `PositionSec == 14`, `DurationSec == 1444`.
  (Если приватной фикстуры нет — Task 8 не завершён; тест на ней должен быть запущен хотя бы раз локально, результат — в отчёте.)
- [ ] **Step 2–5:** FAIL → реализовать → PASS → Commit `feat(core): разбор главной`.

---

### Task 10: Prefs, ContinueModel, DubChooser

**Files:**
- Create: `src/AnimeGo.Core/Prefs.cs`, `ContinueModel.cs`, `DubChooser.cs`; Test: `PrefsTests.cs`, `ContinueModelTests.cs`, `DubChooserTests.cs`

**Interfaces:**
- Produces: `public sealed class Prefs { static Prefs Load(string path) /* нет файла или битый → пустой */; void Save(); string Cookies; bool SkippedLogin; string Quality /* "auto" по умолчанию */; DubPref Dub(string userKey, int animeId); void SetMainDub(string userKey, int animeId, string key); void SetFallbackDub(...); Seen Seen(string userKey, int animeId); void SetSeen(string userKey, int animeId, int episode, int seconds, DateTime atUtc); void RemoveSeen(string userKey, int animeId); }`; `public sealed class DubPref { string Main; string Fallback; }`; `public sealed class Seen { int Episode; int Seconds; DateTime AtUtc; }`. Формат файла — JSON через `Json.Serialize` DTO с `List<...>` (DataContract не умеет словари с int-ключами — хранить списки записей).
- `public static class ContinueModel { ContinueRows Split(IReadOnlyList<ContinueCard> cards, Prefs p, string userKey, DateTime nowUtc) }` → `ContinueRows { IReadOnlyList<ContinueCard> Active; IReadOnlyList<ContinueCard> Stale; }`. Правила: карточка без записи `Seen` → `SetSeen(now)`, Active; запись есть и `(episode, seconds)` отличаются → `SetSeen(now)`, Active; совпадают и `now - AtUtc >= 14 дней` → Stale, иначе Active; записи `Seen`, которых нет среди карточек, удаляются. Порядок — как у сайта.
- `public static class DubChooser { DubDecision Choose(DubPref pref, IReadOnlyList<VideoSource> sourcesForEpisode) }` → `DubDecision { string TranslationKey /* null = спросить */; bool UsedFallback; IReadOnlyList<string> Available /* ключи озвучек, у которых есть cvh или aniboom */ }`. Порядок: `Main`, если у неё есть источник cvh/aniboom → он; иначе `Fallback` при том же условии → `UsedFallback = true`; иначе если `Main == null && Fallback == null` и `Available.Count > 0` → первая доступная озвучка в порядке сайта (`UsedFallback = false`); иначе `null`.

- [ ] **Step 1: Тесты** — `Prefs`: `Load` несуществующего пути → `Quality == "auto"`; `SetMainDub("u1",3547,"59")`, `Save`, `Load` → `Dub("u1",3547).Main == "59"`, `Dub("guest",3547).Main == null`. `ContinueModel`: новая карточка → Active и `Seen.AtUtc == now`; та же карточка через 15 дней без изменений → Stale; та же через 15 дней с другой позицией → Active; карточка пропала → `Seen(...) == null`. `DubChooser`: main есть → её ключ; main без источников, fallback есть → fallback, `UsedFallback`; обеих нет, prefs пустые → первая доступная; обеих нет, prefs заданы → `null`, `Available` без kodik-only озвучек.
- [ ] **Step 2–5:** FAIL → реализовать → PASS → Commit `feat(core): локальные настройки, «давно не смотрели», выбор озвучки`.

---

### Task 11: ProgressTracker и WatchedTracker

**Files:**
- Create: `src/AnimeGo.Core/ProgressTracker.cs`; Test: `ProgressTrackerTests.cs`

**Interfaces:**
- Produces: `public sealed class ProgressTracker { ProgressTracker(int durationSec); void Tick(int positionSec, DateTime nowUtc) /* из таймера раз в секунду */; bool OnPause(); bool OnSeek(int newPositionSec); bool OnEnded(); bool OnExit(); int LastSent; }` — методы возвращают `true`, если **сейчас** надо отправить прогресс с текущей позицией. Правила из JS сайта: первая отправка при позиции > 0 (один раз); `Tick` → отправка, если `|pos − LastSent| ≥ 5` и прошло ≥ 5 с с прошлой отправки; `OnPause/OnSeek/OnEnded/OnExit` → отправка, если позиция > 0 и `pos != LastSent` (OnEnded/OnExit — всегда при позиции > 0).
  `public sealed class WatchedTracker { WatchedTracker(int durationSec); bool Tick(int positionSec) /* true один раз, когда суммарно просмотрено ≥ 80 % длительности; перемотка не засчитывается: считаем секунды, пришедшие последовательно (разница с прошлым tick ≤ 2 с) */; bool Fired; }`.

- [ ] **Step 1: Тесты** — `Tick(1)` → true (первая); `Tick(3)` → false; `Tick(7)` через 6 с → true; `OnPause()` сразу после отправки → false; `OnSeek(100)` → true; `OnExit()` при `pos == LastSent` и `pos > 0` → true; `OnExit()` при позиции 0 → false. `WatchedTracker(100)`: 79 последовательных секунд → false; 80 → true; затем всегда false; перемотка с 10 на 90 и 5 секунд дальше → false.
- [ ] **Step 2–5:** FAIL → реализовать → PASS → Commit `feat(core): правила отправки прогресса и отметки`.

---

### Task 12: Seasons — ряды серий по сезонам

**Files:**
- Create: `src/AnimeGo.Core/Seasons.cs`; Test: `SeasonsTests.cs`

**Interfaces:**
- Produces: `public sealed class Season { int AnimeId; string Title; int Order; bool IsCurrent; }`; `public static class Seasons { IReadOnlyList<Season> Build(AnimeInfo current) }` — текущий тайтл плюс `Related`, чьё название совпадает с базовым именем текущего (название без завершающих ` N`/` 2 часть`/` OVA` и т.п. — сравнение по префиксу до первой цифры, без регистра); порядок по числу в конце названия (нет числа → 1), текущий — `IsCurrent`. Серии сезона грузятся экраном через `SiteApi.PlayerAsync(season.AnimeId)` лениво.

- [ ] **Step 1: Тест** на `AnimeParser.Parse(anime_3547.html)`: `Build` даёт ≥ 2 сезона, содержит `AnimeId 2817` с `Order 2` и `3547` с `Order 3, IsCurrent`; связанный тайтл с другим базовым названием (добавить в тест искусственный `RelatedTitle { Title = "Другое" }`) не попадает.
- [ ] **Step 2–5:** FAIL → реализовать → PASS → Commit `feat(core): сезоны из связанного`.

---

### Task 13: Tv-каркас, DevLog, ImageCache и проверка плеера на ТВ

**Files:**
- Create/Modify: `src/AnimeGo.Tv/Program.cs`, `Theme.cs`, `Widgets.cs`, `DevLog.cs`, `ImageCache.cs`, `VideoView.cs`, `MessageView.cs` (копия из домофона: текст + кнопки «Повторить»/«Назад»), `Toast.cs` (`Toast.Show(Widgets, string text, int ms = 4000)` — плашка снизу по центру), `SmokeScreen.cs` (временный)

**Interfaces:**
- Produces: `interface IScreen { void Show(); void Hide(); bool OnKey(string key); }`; `static class Keys { Up, Down, Left, Right, Ok="Return", Back="XF86Back", Play="XF86AudioPlay", Pause="XF86AudioPause", PlayPause="XF86PlayBack", Stop="XF86AudioStop", FF="XF86AudioNext", Rew="XF86AudioPrev" }` (имена проверить DevLog'ом по нажатию, поправить); `App : CoreUIApplication { Window Win; SiteApi Api; Prefs Prefs; User User; string UserKey => User?.Id.ToString() ?? "guest"; void Navigate(IScreen); static void Ui(Action); }`; `Theme` — цвета из Global Constraints + `T(text, px, color, bold, align)`; `Widgets` — как в домофоне + `Image Picture(...)` и `Rectangle Focus(...)` (белая рамка: прямоугольник белый под карточкой, карточка на 4 px меньше с зазором тёмным); `ImageCache { void Set(Image img, string url) /* файл в DirectoryInfo.Cache по sha1(url), скачивает в фоне, ставит на UI-потоке, если img ещё жив */ }`; `VideoView` как в домофоне: `PlayAsync(string url, int startSec)`, `Pause() Resume() Seek(int sec) Stop()`, `int PositionSec`, `int DurationSec`, события `Playing`, `Ended(string why)`, `Error(string)`.
- `Prefs` хранится в `DirectoryInfo.Data + "/prefs.json"`; куки из `Prefs.Cookies` → `CookieJar.Import`.

- [ ] **Step 1: Собрать каркас** (копия структуры домофона с новыми именами), `SmokeScreen`: фон, надпись «AnimeGo», при OK — в фоне `SiteApi.PlayerAsync(3547)` → первый `cvh`-источник → `CvhStreamsAsync` → `VideoView.PlayAsync(Pick("auto"))`; ←→ — `Seek(±10)`; «Назад» — выход. Каждая клавиша пишет `DevLog.Write("key " + name)`.
- [ ] **Step 2: Запустить** `node scripts/devlog.mjs`, затем `scripts/deploy.sh Debug`. Проверить в логе: время от `PlayAsync` до `Playing` < 5 с; `Seek` на HLS работает (позиция меняется за < 2 с); имена клавиш пульта (Play/Pause/FF/REW). Если HLS не стартует или перемотка медленная — переключить на `Pick("1080p")` (MP4) и записать вывод в `CLAUDE.md` «Грабли».
- [ ] **Step 3: Повторить с AniBoom** (`AniboomStreamsAsync`): если плеер не тянет раздельную аудиодорожку — записать в `CLAUDE.md`, AniBoom в Task 16 использовать только как источник после CVH и только если проверка прошла.
- [ ] **Step 4: Commit** `feat(tv): каркас приложения и проверка плеера`.

---

### Task 14: LoginScreen и сессия

**Files:**
- Create: `src/AnimeGo.Tv/Keyboard.cs`, `LoginScreen.cs`; Modify: `Program.cs`

**Interfaces:**
- Produces: `Keyboard : Entry-обёртка { Keyboard(Window, Widgets, int x,int y,int w,int h, bool password); string Text; void Focus(); event Action Done /* Return на клавиатуре */ }` — ElmSharp `Entry` с `IsSingleLine`, `IsPassword`, `SetInputPanelEnabled(true)`; экранная клавиатура ТВ появляется при фокусе `Entry`.
- `LoginScreen(App)` по макету `Login.dc.html`: слева текст, справа два поля, кнопки «Войти» и «Продолжить без входа», строка ошибки `RuText.BadLogin = "Неверный логин или пароль"` / `RuText.NoConnection`. Фокус: поле логина → поле пароля → Войти → Без входа (↑↓), OK на поле — открыть клавиатуру.
- `App.OnCreate`: загрузить `Prefs`; если куки есть → `WhoAmIAsync()` в фоне → `User`, главная; если `Unauthorized` → очистить куки, главная гостем; если нет кук и `Prefs.SkippedLogin == false` → `LoginScreen`; после входа `Prefs.Cookies = jar.Export()`, `Save`. «Продолжить без входа» ставит `SkippedLogin = true`.

- [ ] **Step 1: Реализовать.** **Step 2: На ТВ:** ввести настоящие логин/пароль (вводит пользователь) → главная (пока `SmokeScreen` с именем пользователя в DevLog); перезапуск приложения → без ввода, `WhoAmIAsync` вернул пользователя; неверный пароль → строка ошибки. Записать в `API-findings.md` код ответа `/login` и имена кук из DevLog.
- [ ] **Step 3: Commit** `feat(tv): экран входа и сохранение сессии`.

---

### Task 15: HomeScreen

**Files:**
- Create: `src/AnimeGo.Tv/HomeScreen.cs`, `Row.cs`; Modify: `Program.cs` (`ShowHome`)

**Interfaces:**
- Produces: `Row` — горизонтальный ряд карточек с фокусом и прокруткой: `Row(Widgets ui, int y, int cardW, int cardH, int gap, string title)`, `void SetItems(IReadOnlyList<RowItem>)`, `int Focus`, `void Move(int dx)`, `Action<RowItem> OnOk`; `RowItem { string Title; string Subtitle; string ImageUrl; double Progress (−1 = без полосы); string Badge; string TimeLabel; object Tag; }`. Геометрия из макета: ряд 1 — 400×225, зазор 32, под картинкой название 22 px и подпись 18 px; постеры — 140×196, зазор 32; жанры — кнопки высотой 64.
- `HomeScreen(App)`: шапка (логотип, вкладки, имя/«Войти»), ряды по спеке §5.2; вертикальная прокрутка содержимого при смене ряда (сдвиг всех виджетов на `y`); данные: `Api.GetHtmlAsync("/")` → `HomeParser.Parse` → `ContinueModel.Split` → ряды; гость — без рядов 1–2. Ошибка загрузки → `MessageView` (копия из домофона) с «Повторить». OK: ряд «Продолжить»/«Давно не смотрели» → `PlayerScreen(animeId, episodeNumber, positionSec)`; жанр → `GridScreen(url, title)`; постер → `TitleScreen(animeId)`; «Поиск»/«Каталог»/«Мой список» → `GridScreen` (Task 16); имя → меню «Выйти» (OK) → `LogoutAsync`, куки стереть, `User = null`, перерисовать. «Назад» — выход.
- «Новая серия»: для карточек с `Progress ≥ 0.8` в фоне `PlayerAsync(animeId)`; если есть серия с номером `> EpisodeNumber` → `Badge = RuText.NewEpisodeBadge`, и OK открывает её с 0.

- [ ] **Step 1: Реализовать** `Row` и `HomeScreen`. **Step 2: На ТВ:** сверить с макетом `Main.dc.html` (скриншотов нет — глазами по пунктам: отступы одинаковые, фокус-рамка, подписи, ряды пустые скрыты); DevLog без исключений; гость видит жанры и сезон.
- [ ] **Step 3: Commit** `feat(tv): главный экран`.

---

### Task 16: GridScreen и TitleScreen

**Files:**
- Create: `src/AnimeGo.Tv/GridScreen.cs`, `TitleScreen.cs`

**Interfaces:**
- `GridScreen(App, GridSource src)`; `GridSource { string Title; string FirstPageUrl /* "/anime/status/ongoing", "/anime/genre/action", "/anime/top-250", "/profile/?type=1", null = поиск */ }`: сетка 10×N постеров 140×196 (зазор 32), заголовок; первая страница — `GetHtmlAsync` → `CatalogParser.Cards` + `Paging`; при фокусе на последней строке и `HasMore` — `CatalogPageAsync`. Поиск: наверху `Keyboard`, запрос после 2+ символов и 500 мс паузы, `SearchAsync`. «Мой список»: вкладки сверху (←→ на строке вкладок), `/profile/?type=N` — карточки парсить по `a.user-mylist__picture` (добавить в `CatalogParser.Cards` поддержку этого класса; тест `[SkippableFact]` на приватной фикстуре `Fixtures/private/mylist_all.html`, которую пользователь сохраняет через `scripts/login-sample.mjs --path "/profile/?type=all" --out mylist_all.html`; проверять только структуру: `≥ 1` карточка, `Id > 0`, `Title` непустой).
- `TitleScreen(App, int animeId)`: постер слева (250×350), справа название, оригинал, `Kind · Year · жанры`, описание 3 строки, кнопки «Смотреть» (→ `PlayerScreen` с `ContinuationUrl` сайта: `GET continuation` → `item.episode`, иначе серия 1) и «Серии» (→ тот же список серий, что в плеере: открыть `PlayerScreen` с панелью серий сразу).

- [ ] **Step 1: Расширить скрипт и `CatalogParser`** (тест на `mylist_all.html`: ≥ 1 карточка с `Id 109`). **Step 2: Реализовать экраны.** **Step 3: На ТВ:** жанр → сетка → догрузка второй страницы при прокрутке вниз; поиск «океан» → ≥ 3 результата; «Мой список» → вкладки; OK на постере → тайтл → «Смотреть».
- [ ] **Step 4: Commit** `feat(tv): каталог, поиск, мой список, страница тайтла`.

---

### Task 17: PlayerScreen — воспроизведение, панель, серии, прогресс

**Files:**
- Create: `src/AnimeGo.Tv/PlayerScreen.cs`, `EpisodePanel.cs`; Modify: `VideoView.cs`

**Interfaces:**
- `PlayerScreen(App, int animeId, int episodeNumber, int startSec, bool openEpisodes = false)`.
- Запуск серии (метод `PlayEpisodeAsync(int number, int startSec, string forcedTranslationKey = null)`): `PlayerAsync(animeId)` (кешировать на время экрана) → `EpisodeVideosAsync(episode.Id)` → `DubChooser.Choose(Prefs.Dub(UserKey, animeId), videos.Sources)` (или `forcedTranslationKey`) → источники выбранной озвучки в порядке `cvh`, `aniboom` → `StreamSet` → `Pick(Prefs.Quality)` → `VideoView.PlayAsync(url, startSec)`. Ошибка источника (`ApiException`, `Error` плеера, не стартовал за 10 с) → следующий источник с `Toast(RuText.SwitchedSource)`; кончились → панель озвучек (Task 18) с подписью `RuText.SourceFailed = "Не удалось включить видео в этой озвучке"`. Если `Choose` вернул `null` → панель озвучек (Task 18). `UsedFallback` → уведомление `RuText.SwitchedDub` на 4 с.
- Клавиши по спеке §5.6: панель скрыта — OK пауза/плей; ←→ ±10 с, удержание (повтор KeyDown чаще 300 мс) → шаг ×2 каждую секунду до ×8 (т.е. 20/40/80 с на тик), позиция на полосе обновляется, применяется `Seek` при отпускании (KeyUp — `EcoreEventType.KeyUp`) ; ↓/любая другая → показать панель. Панель показана: фокус по макету `Player.dc.html`: полоса → ряд кнопок (Пауза, Озвучка, Качество, Следующая, Пропустить опенинг) → ряды сезонов (`Seasons.Build(AnimeParser.Parse(GetHtmlAsync(info.Url)))`, серии каждого сезона — ленивый `PlayerAsync(season.AnimeId)`; плитки 200×112, текущая с рамкой, просмотренные с галочкой `✓` и приглушённые). «Назад» — закрыть панель; при скрытой — `OnExit` → отправка прогресса → предыдущий экран. Панель прячется через 5 с бездействия, если фокус на полосе или кнопках.
- Прогресс: таймер 1 с на UI-потоке → `ProgressTracker.Tick(pos)` / `WatchedTracker.Tick(pos)`; при `true` — `PostProgressAsync(episode.Id, translationId, animeId, pos, duration)` в фоне (`translationId` — из `VideoSource.TranslationId`; если `null` — прогресс не слать, DevLog); `WatchedTracker` → `EpisodeVideosAsync` → если `IsWatched != true` → `PostWatchedAsync(episode.Id, translationKey)`; локально `episode.Watched = true`. Гость — ничего не слать. После каждого `Post…` — `Prefs.SetSeen(UserKey, animeId, number, pos, now)`.
- Конец серии: `Ended("completed")` или `pos ≥ duration − 10` → отсчёт 5 с «Следующая серия через N» (кнопки «Смотреть»/«Отмена»); далее `PlayEpisodeAsync(number + 1, 0)`; нет следующей → `TitleScreen`.
- «Пропустить опенинг» → `Seek(pos + 85)`. «Следующая серия» → `PlayEpisodeAsync(number + 1, 0)`.

- [ ] **Step 1: Реализовать.** **Step 2: На ТВ** (DevLog): запуск из «Продолжить» стартует с позиции карточки; ±10; удержание ускоряется; пауза; панель и выбор серии; после 30 с просмотра на сайте (телефон/ПК) карточка обновилась; выход на 3-й секунде — в логе один POST с `currentTime: 3`; серия, досмотренная до 80 % — на сайте галочка, второй POST не ушёл; ещё раз запустить ту же серию — POST `watched` не уходит.
- [ ] **Step 3: Commit** `feat(tv): плеер`.

---

### Task 18: ChoicePanel — озвучка и качество, запасная озвучка

**Files:**
- Create: `src/AnimeGo.Tv/ChoicePanel.cs`; Modify: `PlayerScreen.cs`

**Interfaces:**
- `ChoicePanel(Widgets, string title, string hint, IReadOnlyList<ChoiceItem>)`, `ChoiceItem { string Key; string Title; string Note /* "CVH · Kodik" или "1080p" */; string Mark /* RuText.Main / Fallback / "сейчас" / null */; bool Enabled; string Group /* "ДУБЛЯЖ" / "МНОГОГОЛОСАЯ" / "СУБТИТРЫ" / "" */; }`, `Action<ChoiceItem> OnPick`, `Action<ChoiceItem> OnMakeMain` (кнопка справа в строке; ←→ переключает фокус между строкой и кнопкой), `Action OnClose`. Макет `PlayerDub.dc.html`: панель справа 600 px, подпись ↑↓/OK/Назад внизу.
- Озвучка: список из `PlayerInfo.Translations` с группировкой по `Kind` (если `Kind` пуст — по `voiceType` из CVH-плейлиста, иначе группа «»), `Note` — провайдеры через ` · `, kodik-only → `Enabled = false` с `Note = RuText.Unavailable = "недоступно в приложении"`. Выбор при открытии из кнопки «Озвучка» → `SetMainDub` + перезапуск серии с той же позиции. Выбор при принудительном открытии (`Choose == null` / все источники упали) → `SetFallbackDub` + запуск. `OnMakeMain` → `SetMainDub` без перезапуска, если это текущая; иначе с перезапуском. Если выбранная становится основной, а была запасной — `Fallback` очистить.
- Качество: `auto, 1080p, 720p, 480p, 360p` из `StreamSet.Streams`; выбор → `Prefs.Quality`, перезапуск с той же позиции.
- Автопереход (Task 17): если `Choose` для следующей серии вернул `null` — вместо отсчёта сразу панель озвучек.

- [ ] **Step 1: Реализовать.** **Step 2: На ТВ:** сменить озвучку → видео продолжилось с того же места, в DevLog `SetMainDub`; у тайтла, где у основной студии нет последней серии (найти онгоинг с одной озвучкой на новой серии) — панель с подписью `NoDub`, выбор → запасная; следующая серия с запасной включается молча с уведомлением; «Сделать основной» меняет пометки; качество 480p переключает URL.
- [ ] **Step 3: Commit** `feat(tv): выбор озвучки и качества`.

---

### Task 19: Значок, README, установка на второй ТВ

**Files:**
- Create: `design/logo/animego-icon.png` (512×512: тёмный фон, «A» в акценте — простой, можно SVG → PNG), `src/AnimeGo.Tv/res/animego.png`; Modify: `README.md`, `tizen-manifest.xml` (`version` 1.0.1), `CLAUDE.md` (грабли, найденные в Tasks 13–18)

- [ ] **Step 1:** значок, `version` поднять, переустановить (`sdb uninstall ru.animego.tv`, `deploy.sh Release`).
- [ ] **Step 2: README:** как собрать, `TV=ip:port scripts/deploy.sh Release`, как включить режим разработчика на новом Samsung (Apps → `12345` → Developer mode On → Host PC IP → перезагрузка), `sdb connect`.
- [ ] **Step 3: Второй ТВ** (у друга): пользователь сообщает модель и IP; `TV=<ip>:26101 scripts/deploy.sh Release`; проверка входа и воспроизведения. Если Tizen новее и `.tpk` отказывается ставиться — поднять `api-version` в манифесте.
- [ ] **Step 4: Commit** `chore: значок, README, установка на второй ТВ`.

---

### Task 20: Пропуск опенинга по aniskip (после установки у друга)

**Files:**
- Create: `src/AnimeGo.Core/Aniskip.cs` (+ `AniskipTests.cs` на сохранённом ответе), Modify: `SiteApi.Cdn.cs` (`AniskipAsync(titleId, episode)`), `PlayerScreen`/`PlayerDub` (кнопка и автопоказ), `Prefs` (кеш таймингов на тайтл/серию не нужен — ответ маленький, запрашивать при старте серии).

**Interfaces:**
- `public sealed class SkipInterval { int StartSec; int EndSec; }`; `public static class Aniskip { string Url(int malId, int episode); SkipInterval ParseOpening(string json, int cvhDurationSec) /* берёт op; сдвиг = cvhDuration − episodeLength, применяется при 0 ≤ сдвиг ≤ 60, иначе null */ }`.
- Поведение: при старте серии запросить aniskip по `titleId` из CVH-источника (у AniBoom — нет id → только запомненная позиция). Если интервал есть: кнопка «Пропустить опенинг» видна только внутри `[Start−5, End]`, OK → `Seek(End)`; при `pos` в интервале и без нажатий — ничего не делаем (автопропуск не включаем). Если данных нет — текущая логика (`Prefs.OpeningAt` + 85/90 с).

- [x] **Step 1:** сохранить `research/samples/aniskip_62542_1.json`; тесты: `ParseOpening(json, 1440)` → `164–249` (сдвиг 20 → `184–269`? — проверить правило на реальных числах: episodeLength 1420, cvh 1440 → сдвиг 20); `ParseOpening(json, 1500)` → null (сдвиг 80); `found:false` → null.
- [x] **Step 2:** реализовать, деплой, проверка на ТВ: на серии 2 «Необъятного океана 3» кнопка появляется в опенинге и исчезает после него.
- [x] **Step 3:** Commit `feat: пропуск опенинга по aniskip`.
