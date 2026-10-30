using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AnimeGo.Core;
using ElmSharp;

namespace AnimeGo.Tv
{
    // Постеры и кадры: файл в кеше приложения, имя — sha1(url) + расширение по содержимому. Скачивание в фоне,
    // картинка ставится на UI-потоке, только если Image ещё жив и ждёт именно этот адрес.
    // WebP (а постеры animego только такие) EFL на ТВ не открывает — перегоняем его в PNG через ImageSharp там же, в фоне.
    // Set звать только с UI-потока.
    sealed class ImageCache
    {
        readonly string dir;
        readonly HttpClient http;
        readonly SemaphoreSlim slots = new SemaphoreSlim(4);   // не душить сеть ТВ десятком загрузок разом
        readonly Dictionary<Image, string> wanted = new Dictionary<Image, string>();   // только UI-поток
        readonly Dictionary<string, Task<string>> loading = new Dictionary<string, Task<string>>();   // под lock

        // Кеш на диске растёт без предела (постер PNG ≈ 200 КБ): при старте в фоне, если он больше MaxBytes,
        // удаляем самые давние по времени записи, пока не станет ≤ TrimToBytes. Время записи обновляется при показе
        // (Touch), так что «давние» — это давно не показывавшиеся.
        const long MaxBytes = 120L << 20, TrimToBytes = 90L << 20;
        static readonly TimeSpan TouchEvery = TimeSpan.FromHours(6);

        public ImageCache(string dir)
        {
            this.dir = dir;
            http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", SiteApi.UserAgent);
            Task.Run(() => Trim());
        }

        void Trim()
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                var sw = Stopwatch.StartNew();
                // Метки .bad остались от сборок, где WebP не умели открывать; .tmp — от прерванных загрузок.
                foreach (var f in Directory.GetFiles(dir, "*.bad")) File.Delete(f);
                foreach (var f in Directory.GetFiles(dir, "*.tmp")) File.Delete(f);
                var files = new List<FileInfo>();
                long total = 0;
                foreach (var f in new DirectoryInfo(dir).GetFiles()) { files.Add(f); total += f.Length; }
                var before = total;
                var removed = 0;
                if (total > MaxBytes)
                {
                    files.Sort((a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
                    foreach (var f in files)
                    {
                        if (total <= TrimToBytes) break;
                        try { f.Delete(); total -= f.Length; removed++; } catch { }
                    }
                }
                DevLog.Write("image cache: " + files.Count + " файлов, " + (before >> 20) + " МБ" +
                    (removed > 0 ? ", удалено " + removed + " → " + (total >> 20) + " МБ" : "") + ", " + sw.ElapsedMilliseconds + " мс");
            }
            catch (Exception e) { DevLog.Write("image cache trim: " + e.GetType().Name + " " + e.Message); }
        }

        // Показали картинку из кеша — отметить, что она нужна (не чаще раза в TouchEvery на файл).
        static void Touch(string file) => Task.Run(() =>
        {
            try
            {
                var now = DateTime.UtcNow;
                if (now - File.GetLastWriteTimeUtc(file) > TouchEvery) File.SetLastWriteTimeUtc(file, now);
            }
            catch { }
        });

        // cacheKey — имя в кеше вместо адреса: для ссылок с меняющимся токеном (кадры серий CVH).
        public void Set(Image img, string url, string cacheKey = null)
        {
            if (img == null || string.IsNullOrEmpty(url)) return;
            if (url.StartsWith("//", StringComparison.Ordinal)) url = "https:" + url;
            else if (url.StartsWith("/", StringComparison.Ordinal)) url = SiteApi.Base + url;
            wanted[img] = url;
            var name = Path.Combine(dir, Sha1(cacheKey ?? url));
            var cached = Cached(name);
            if (cached != null) { Touch(cached); Apply(img, url, cached); return; }
            Load(url, name).ContinueWith(t =>
            {
                var file = t.Status == TaskStatus.RanToCompletion ? t.Result : null;
                App.Ui(() => { if (file != null) Apply(img, url, file); else Forget(img, url); });
            });
        }

        // Картинка по cacheKey, если она уже в кеше; false — нет, надо узнать адрес и звать Set.
        public bool TrySetCached(Image img, string cacheKey)
        {
            if (img == null) return false;
            var cached = Cached(Path.Combine(dir, Sha1(cacheKey)));
            if (cached == null) return false;
            Touch(cached);
            wanted[img] = cacheKey;
            Apply(img, cacheKey, cached);
            return true;
        }

        void Apply(Image img, string url, string file)
        {
            if (!wanted.TryGetValue(img, out var u) || u != url) return;   // за это время попросили другой адрес
            wanted.Remove(img);
            if (!img.IsRealized) return;   // экран уже закрыт
            if (img.Load(file)) DevLog.Write("image: ok " + url);
            else
            {
                DevLog.Write("image: не открылся " + url);
                try { File.Delete(file); } catch { }
            }
        }

        void Forget(Image img, string url)
        {
            if (wanted.TryGetValue(img, out var u) && u == url) wanted.Remove(img);
        }

        // Одна загрузка на адрес, даже если его просят несколько карточек.
        Task<string> Load(string url, string name)
        {
            lock (loading)
            {
                if (loading.TryGetValue(url, out var t)) return t;
                t = Task.Run(() => Download(url, name));
                loading[url] = t;
                // Убираем после добавления: иначе быстро упавшая загрузка осталась бы в словаре навсегда.
                t.ContinueWith(_ => { lock (loading) loading.Remove(url); });
                return t;
            }
        }

        // Возвращает путь к файлу или null.
        async Task<string> Download(string url, string name)
        {
            await slots.WaitAsync().ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(dir);
                var bytes = await http.GetByteArrayAsync(url).ConfigureAwait(false);
                if (Placeholders.Contains(Sha1(bytes))) { DevLog.Write("image: заглушка " + url); return null; }
                var ext = Ext(bytes);
                if (ext == ".webp") { bytes = WebpToPng(bytes); ext = ".png"; }
                var file = name + ext;
                var tmp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(tmp, bytes);
                if (File.Exists(file)) File.Delete(tmp);
                else File.Move(tmp, file);
                return file;
            }
            catch (Exception e)
            {
                // Отражение и статические конструкторы ImageSharp заворачивают причину — пишем самую внутреннюю.
                var x = e; while (x.InnerException != null) x = x.InnerException;
                DevLog.Write("image: " + url + " — " + x.GetType().Name + " " + x.Message);
                return null;
            }
            finally { slots.Release(); }
        }

        // Декодирование идёт внутри слота загрузки, так что одновременно их не больше четырёх.
        static byte[] WebpToPng(byte[] webp)
        {
            var sw = Stopwatch.StartNew();
            var d = decoder.Value;
            using (var image = (IDisposable)d.Load.Invoke(null, new object[] { webp }))
            using (var ms = new MemoryStream())
            {
                d.SaveAsPng.Invoke(null, new object[] { image, ms });
                DevLog.Write("webp: decoded in " + sw.ElapsedMilliseconds + " ms, " + d.Width.GetValue(image) + "x" + d.Height.GetValue(image) + ", " + webp.Length / 1024 + " KB");
                return ms.ToArray();
            }
        }

        // ImageSharp 2.x ссылается на System.Runtime.CompilerServices.Unsafe 5.0.0.0, а на ТВ своя, старее, и подменить её
        // в общем контексте нельзя (FileLoadException 0x80131040). Поэтому ImageSharp и его Unsafe грузим в отдельный
        // AssemblyLoadContext из папки приложения и зовём через отражение: прямая ссылка утянула бы его в общий контекст.
        static readonly Lazy<WebpDecoder> decoder = new Lazy<WebpDecoder>(() => new WebpDecoder());

        sealed class WebpDecoder
        {
            public readonly MethodInfo Load, SaveAsPng;
            public readonly PropertyInfo Width, Height;

            public WebpDecoder()
            {
                var bin = Path.GetDirectoryName(typeof(ImageCache).Assembly.Location);
                var ctx = new LocalContext(bin);
                var asm = ctx.LoadFromAssemblyPath(Path.Combine(bin, "SixLabors.ImageSharp.dll"));
                var image = asm.GetType("SixLabors.ImageSharp.Image", true);
                // У Load и SaveAsPng есть обобщённые двойники с теми же параметрами — GetMethod(name, types) на них спотыкается.
                Load = Find(image, "Load", typeof(byte[]));
                SaveAsPng = Find(asm.GetType("SixLabors.ImageSharp.ImageExtensions", true), "SaveAsPng", image, typeof(System.IO.Stream));
                Width = image.GetProperty("Width");
                Height = image.GetProperty("Height");
            }

            static MethodInfo Find(Type type, string name, params Type[] args)
            {
                foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != name || m.IsGenericMethodDefinition) continue;
                    var ps = m.GetParameters();
                    if (ps.Length != args.Length) continue;
                    var same = true;
                    for (var i = 0; i < ps.Length; i++) same &= ps[i].ParameterType == args[i];
                    if (same) return m;
                }
                throw new MissingMethodException(type.FullName, name);
            }
        }

        sealed class LocalContext : AssemblyLoadContext
        {
            static readonly string[] Own = { "SixLabors.ImageSharp", "System.Runtime.CompilerServices.Unsafe", "System.Text.Encoding.CodePages" };
            readonly string bin;

            public LocalContext(string bin) { this.bin = bin; }

            protected override Assembly Load(AssemblyName name)
            {
                if (Array.IndexOf(Own, name.Name) < 0) return null;   // остальное — из общего контекста
                var path = Path.Combine(bin, name.Name + ".dll");
                return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
            }
        }

        // EFL выбирает загрузчик по расширению, а в адресах картинок его нет — берём по первым байтам.
        static readonly string[] Exts = { ".jpg", ".png", ".gif" };

        static string Ext(byte[] b)
        {
            if (b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return ".webp";
            if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ".jpg";
            if (b.Length >= 4 && b[0] == 0x89 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G') return ".png";
            if (b.Length >= 3 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F') return ".gif";
            throw new InvalidDataException("не картинка");
        }

        static string Cached(string name)
        {
            foreach (var e in Exts) if (File.Exists(name + e)) return name + e;
            return null;
        }

        // Картинки-заглушки CDN, которые показывать нельзя: AniBoom вместо кадра серии отдаёт серое «IMAGE NOT FOUND»
        // (hqdefault.webp, JPEG 320×180, 20 795 байт) — такой ответ считаем отсутствием кадра.
        static readonly HashSet<string> Placeholders = new HashSet<string> { "0adabf35d51d9b788fe3fa4b367e442cc650ff3f" };

        static string Sha1(string url) => Sha1(Encoding.UTF8.GetBytes(url));

        static string Sha1(byte[] data)
        {
            using (var sha = SHA1.Create())
            {
                var hash = sha.ComputeHash(data);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
