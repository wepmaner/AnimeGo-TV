# Разбор animego.me (27.10.2026)

Сайт на Symfony + Turbo/Stimulus: страницы рендерятся на сервере, в JSON-ответах лежит HTML-фрагмент в `data.content`.
Ни в одном ответе нет капчи. Перед сайтом стоит DDoS-Guard (куки `__ddg*`), но обычный `curl` с браузерным User-Agent проходит без проблем.
Образцы ответов лежат в `research/samples/`.

Общие заголовки для AJAX: `X-Requested-With: XMLHttpRequest`, `Accept: application/json`.
Без них часть URL возвращает HTML или 404 (например, `/player/videos/{id}`).

## Каталог (без входа)

| Что | URL | Ответ |
|---|---|---|
| Главная | `GET /` | HTML. Секции: «Аниме осеннего сезона», «Продолжить просмотр» (только после входа), «Расписание», «Обновления аниме», «Новое на сайте» |
| Онгоинги | `GET /anime/status/ongoing` | HTML, 20 карточек на странице |
| Следующие страницы | `GET /anime/status/ongoing/{N}?entities=true` + AJAX-заголовки | JSON `{status, data:{content}}`, в content HTML карточек `.ani-list__item` |
| Сезон | `/anime/season/2026/fall`, `/anime/season/2026` | то же |
| Топ | `/anime/top-250` | то же |
| Жанр / тип | `/anime/genre/{slug}`, `/anime/type/{tv,ona,…}` | то же |
| Все | `/anime` | то же |
| Поиск | `GET /search/all?q=…` | HTML со ссылками `/anime/{slug}-{id}` |

Параметры пагинации берутся из атрибутов `data-infinite-scroll-*` на странице: `url-template-value="/anime/status/ongoing/__page__"`, `page-size-value="20"`, `total-items-value`, `has-more-value`, `params-value={"entities":"true"}`.

Карточка `.ani-list__item` содержит: ссылку `/anime/{slug}-{id}`, постер `https://img.cdngos.com/v/250x350/anime/…`, рейтинг (`.rating-badge`), русское и оригинальное название, тип, жанры.
Размер постера задаётся в URL: `/v/{W}x{H}/` (встречаются 120x168, 250x350, 640x360).

## Страница аниме и серии

- `GET /anime/{slug}-{animeId}` — HTML: описание, кадры и т.п.
- `GET /player/{animeId}` (AJAX) — JSON, в `data.content` HTML плеера. Из него берём:
  - на корневом `.player-video`: `data-anime-player-episodes-total-value`, `…-episodes-pages-value` (страницы по 100 серий: `[{page,firstNumber,lastNumber,count}]`), `…-episodes-url-value="/player/{id}/episodes"`, `…-continuation-url-value="/player/{id}/continuation/1"`, `…-initial-provider-priority-value=["cvh","aniboom"]`, `data-anime-episode-watched-url-value="/anime/episode/watched"`;
  - серии: элементы с `data-episode="<episodeId>"`, `data-episode-number`, `data-episode-watched="true|false"`, `data-episode-type`;
  - озвучки: `data-translation` (ключ) и `data-translation-title`;
  - источники: кнопки `data-provider-slug` (`cvh` | `aniboom` | `kodik`), `data-player` (URL iframe), `data-translation-id`, `data-ptranslation` (связь с озвучкой), `data-provider-title`.
- `GET /player/videos/{episodeId}` (AJAX) — JSON `{numVideos, isWatched, bookmarks, content, episode_information}`, в content тот же список озвучек и источников, но для выбранной серии.
- `GET /player/{animeId}/continuation/1` (AJAX) — `{"item":{"episode":1,"poster":"…","duration":1440}}`, то есть с какой серии продолжать.

Пример `data-player` у CVH: `//animego.me/cdn-iframe/{titleId}/{Озвучка}/{season}/{episode}`.

## Видео: CVH (cdnvideohub) — основной источник, лучший вариант

Страница `cdn-iframe` содержит только `<video-player data-title-id="62542" data-publisher-id="747" data-aggregator="mali" episode="1" priority-voice="Jam Club">`. Сами данные плеер берёт из открытого API:

1. `GET https://plapi.cdnvideohub.com/api/v1/player/sv/playlist?pub=747&aggr=mali&id={titleId}` (с `Referer: https://animego.me/`)
   → `{titleName, isSerial, items:[{cvhId, vkId, voiceStudio, voiceType, season, episode}]}`, то есть все серии во всех озвучках.
2. `GET https://plapi.cdnvideohub.com/api/v1/player/sv/video/{vkId}`
   → `{duration, thumbUrl, failoverHost, sources:{hlsUrl, dashUrl, mpegFullHdUrl, mpegHighUrl, mpegMediumUrl, mpegLowUrl, …}}`.
   Ссылки ведут на `*.okcdn.ru` (видео-CDN ВК/ОК).
3. HLS (`application/x-mpegURL`) и MP4 (поддерживается Range, `206`) **отдаются без Referer и куки**, так что их можно передавать прямо в плеер ТВ.
   Ссылка подписана (`expires`, `srcIp`): её нужно запрашивать с того же внешнего IP, что и смотреть. ТВ и так ходит со своего IP. Перед воспроизведением всегда берём свежую ссылку.

`titleId` (62542) — это не `animeId` сайта (3547). Он есть только в `data-player` у CVH-кнопок в ответе `/player/{animeId}`.

## Видео: AniBoom — запасной источник

`data-player` = `//aniboom.one/embed/{abId}?episode=N&translation=T&…`. Страницу запрашиваем с `Referer: https://animego.me/`.
В HTML есть `data-parameters` (JSON в HTML-entities) с полями `hls.src` (`…/master.m3u8`), `dash.src`, `duration`, `poster`, `episodePicker.endpoint`.
`master.m3u8` отдаётся без Referer. Особенность: аудио идёт отдельной дорожкой (`EXT-X-MEDIA TYPE=AUDIO`); нужно проверить, как это проигрывает плеер ТВ.

## Видео: Kodik — пока не разбирали

`data-player` = `//kodikplayer.com/seria/{id}/{hash}/720p`. У Kodik обфусцированный API, который часто меняется. Займёмся им, только если у какого-то тайтла не окажется CVH/AniBoom.

## Аккаунт и прогресс (нужен вход)

### Вход
Форма `POST /login` (`application/x-www-form-urlencoded`): `_username`, `_password`, `_remember_me=on`, `_csrf_token`.
CSRF без сессии (Symfony stateless): в форме лежит `value="csrf-token"`, а JS:
1. генерирует случайный токен `T` (base64 из 18 случайных байт);
2. ставит куку `__Host-csrf-token_T=csrf-token` (на https);
3. отправляет `_csrf_token=T`.

Приложение делает то же самое у себя. После входа нужно сохранить куки (сессия + `REMEMBERME`) и дальше слать их во все запросы.

Что видно в записи настоящего входа: форму отправляет Turbo через fetch, браузер идёт по редиректу, в логе `POST /login → 200`, затем `GET /`.
В нативном клиенте ожидаем `302` (успех → `/`, ошибка → `/login`). Проверяем не по коду ответа, а по признаку входа ниже.
**Проверено с ПК 28.10.2026 (`scripts/login-sample.mjs`):** `POST /login` → `302`, `Location: https://animego.me/`; выданы куки `PHPSESSID`, `REMEMBERME`, `vid`, плюс `__ddg1_`/`__ddg8_`/`__ddg9_`/`__ddg10_` (DDoS-Guard). Следующий `GET /` с этими куками отдаёт страницу с `data-user` и секцией «Продолжить просмотр». При ошибке пароля ожидаем `302 → /login`.
Атрибут `data-user` записан с HTML-кодированием: `data-user="&#x7B;&quot;id&quot;:…"` — перед разбором JSON нужно декодировать entities (`Html.Attr` это делает).

**Признак входа:** у любой страницы на `<html>` есть атрибут `data-user='{"id":…,"username":"…"}'`; у гостя его нет.
Здесь же берём имя пользователя для экрана. Проверено.

### «Продолжить просмотр»
Отдельного API нет, это секция главной страницы `/` (с куками), `.continue-watching-section`, карточки `a.cw__link`:
- `href="/anime/{slug}-{animeId}#player"`;
- текст `Серия 22` / `00:14 / 24:04` / название;
- полоска `style="width: 0.97%"`;
- кадр серии `img.cdngos.com/episode/…`;
- удалить из списка: `data-ajax-url="/video/progress/remove"`, `data-anime-id`.

### Отправка позиции
Сайт шлёт её через `navigator.sendBeacon`, без CSRF-заголовка:
`POST /video/progress`, `Content-Type: application/json`,
`{"episodeId":44100,"translationId":59,"animeId":3547,"currentTime":123,"duration":1440}` (секунды, целые).
Отправляет каждые ≥5 с изменения позиции, при паузе, перемотке, окончании и закрытии.
Проверено вживую: после 5 минут просмотра в браузере тайтл встал первым в «Продолжить просмотр» с позицией `05:08 / 23:59`.
Сам `sendBeacon` расширение Chrome не показывает; формат взят из JS сайта.

### Отметка «серия просмотрена»
`POST /anime/episode/watched`, `application/x-www-form-urlencoded`, `id={episodeId}&translationId={translationKey}`.
Ответ: JSON `{status, data:{watchedEpisodeNumber, …}}`. Это **переключатель** (toggle), поэтому отправляем, только если `data-episode-watched="false"`.
Сайт ставит отметку автоматически, когда просмотрено ≥80% серии.

### Мой список (на сайте «избранного» отдельно нет, есть только этот список)
**Чтение:** `GET /profile/?type={all|1..6}` (с куками), HTML. Типы:
`1` Смотрю, `2` Просмотрено, `3` Отложено, `4` Брошено, `5` Запланировано, `6` Пересматриваю, `all` Все.
Счётчики видны в табах (`a[href*="type="]`, текст вида «Смотрю 11»).
- Элемент: `.user-mylist__item`, внутри `a.user-mylist__picture[href="/anime/{slug}-{id}"]` с постером 250x350, название, оригинальное название, «Моя оценка», прогресс «Начать 0 / 25» или «Просмотрено 11 / 19».
- 20 штук на страницу. Следующая страница — turbo-frame `profile-my-list-page-2` с `src="/user/mylist/{userId}/2?type=all"`; `GET /profile/?type=all&page=2` тоже работает.
- Сортировки в интерфейсе нет. Порядок, похоже, не по дате (тайтлы Re:Zero 1–4 идут подряд), но это не проверено.

**Добавить:** `POST /mylist/anime/{animeId}/{1..6}/add`, нужен CSRF из `data-csrf-token` кнопки на странице аниме.

## Жанры (для категорий на главной)
`/anime/genre/{slug}`: action, adventure, comedy, fantasy, horror, josei, kids, mystery, romance, sci-fi, seinen, shounen, supernatural, suspense, … (полный список в фильтре каталога `/anime`).
Формат как у каталога, пагинация JSON `…/{N}?entities=true`.

## Что ещё не проверено

- Ответ `/login` и поведение кук (нужно ввести логин; сделаем из приложения на ТВ).
- Принимает ли `/video/progress` наш запрос не из браузера (ожидаем да: обычный POST с куками).
- Как плеер ТВ (AVPlay / `Tizen.Multimedia.Player`) проигрывает HLS от okcdn и AniBoom с отдельным аудио.

## Тайминги опенинга (проверено 28.10.2026)

- У CVH/AniBoom данных о границах опенинга нет (`skippedSegments` в плейлистах CVH для animego всегда отсутствует).
- **id тайтла CVH (`cdn-iframe/{titleId}`) совпадает с id Shikimori/MAL**: 62542 = Grand Blue Season 3, 61316 = Re:Zero 4th Season, 59986 = Grand Blue Season 2.
- **aniskip**: `GET https://api.aniskip.com/v2/skip-times/{malId}/{episode}?types=op&types=ed&episodeLength=0` → `{found, results:[{skipType:"op"|"ed", interval:{startTime,endTime}, episodeLength}]}`. Покрытие неполное (62542: серии 1–2 есть, 5 нет; 61316: 12 есть, 1 нет). Качество разное (у 62542/1 ED явно от другого релиза).
- Длительность серии зависит от озвучки: JAM CLUB 1462 с, Студийная Банда 1440, AniLiberty 1441, aniskip 1420–1440 → у некоторых студий вставка в начале (~22 с), опенинг сдвинут. Правило: сдвигать тайминги на `durCVH − episodeLength`, если 0 ≤ сдвиг ≤ 60 с; иначе не доверять.
