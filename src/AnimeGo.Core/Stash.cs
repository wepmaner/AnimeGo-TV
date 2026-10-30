using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;

namespace AnimeGo.Core
{
    public static class AppInfo
    {
        public const string Package = "ru.animego.tv";
        public const string Version = "1.0.6";   // = version в tizen-manifest.xml (проверяет тест; меняет scripts/release.sh)
    }

    // Ответ REST-API телевизора http://<tv>:8001/api/v2/ — нужное из него.
    public sealed class TvInfo
    {
        public bool DeveloperMode;
        public string DeveloperIp;   // ПК, указанный в режиме разработчика; null — нет
        public string Model;         // modelName ("UE50TU7140UXRU")

        // null — не JSON или нет блока device.
        public static TvInfo Parse(string json)
        {
            TvApiDto d;
            try { d = Json.Parse<TvApiDto>(json ?? ""); }
            catch (Exception) { return null; }   // тип ошибки разбора зависит от рантайма
            if (d?.Device == null) return null;
            var ip = (d.Device.DeveloperIp ?? "").Trim();
            return new TvInfo
            {
                DeveloperMode = d.Device.DeveloperMode == "1",
                DeveloperIp = ip.Length == 0 ? null : ip,
                Model = d.Device.ModelName,
            };
        }

        // Куда слать логи: только в режиме разработчика и с указанным IP.
        public string LogHost => DeveloperMode && DeveloperIp != null ? DeveloperIp : null;
    }

    // Последние Capacity строк лога и какие из них ещё не ушли в Stash. Потокобезопасен.
    public sealed class LogBuffer
    {
        public const int Capacity = 500;
        readonly LinkedList<(long Seq, string Line)> lines = new LinkedList<(long, string)>();
        long seq, sent;

        public void Add(string line)
        {
            lock (lines)
            {
                lines.AddLast((++seq, line));
                if (lines.Count > Capacity) lines.RemoveFirst();
            }
        }

        // Неотправленные строки (не больше Capacity) и метка, которую передать в MarkSent после удачной отправки.
        public (string Text, long UpTo) Unsent()
        {
            lock (lines)
            {
                var list = new List<string>();
                foreach (var l in lines) if (l.Seq > sent) list.Add(l.Line);
                return (string.Join("\n", list), seq);
            }
        }

        public void MarkSent(long upTo)
        {
            lock (lines) if (upTo > sent) sent = upTo;
        }

        public bool HasUnsent
        {
            get { lock (lines) return seq > sent; }
        }
    }

    // JSON протокола со Stash (лаунчер на ПК): /hello на ПК и сервер входа 8779 на ТВ.
    public static class StashProtocol
    {
        public static string Hello(string version, string model, string tizen, User user) => Json.Serialize(new HelloDto
        {
            App = AppInfo.Package, Version = version, Model = model, Tizen = tizen, User = UserDto(user),
        });

        public static string Status(string version) => Json.Serialize(new StatusDto { Screen = "login", Version = version });

        public static string LoginOk(User user) => Json.Serialize(new LoginResultDto { Ok = true, User = UserDto(user) });

        public static string LoginFail(string error) => Json.Serialize(new LoginResultDto { Ok = false, Error = error });

        // {"login":"…","password":"…"} → пара; null — не JSON или пустые поля.
        public static (string Login, string Password)? ParseLogin(string json)
        {
            LoginRequestDto r;
            try { r = Json.Parse<LoginRequestDto>(json ?? ""); }
            catch (Exception) { return null; }
            var login = r?.Login?.Trim();
            if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(r.Password)) return null;
            return (login, r.Password);
        }

        static StashUserDto UserDto(User u) =>
            u == null ? null : new StashUserDto { Id = u.Id.ToString(CultureInfo.InvariantCulture), Name = u.Name };
    }

    [DataContract]
    internal class TvApiDto
    {
        [DataMember(Name = "device")] public TvDeviceDto Device { get; set; }
    }

    [DataContract]
    internal class TvDeviceDto
    {
        [DataMember(Name = "developerMode")] public string DeveloperMode { get; set; }
        [DataMember(Name = "developerIP")] public string DeveloperIp { get; set; }
        [DataMember(Name = "modelName")] public string ModelName { get; set; }
    }

    [DataContract]
    internal class HelloDto
    {
        [DataMember(Name = "app", Order = 0)] public string App { get; set; }
        [DataMember(Name = "version", Order = 1)] public string Version { get; set; }
        [DataMember(Name = "model", Order = 2)] public string Model { get; set; }
        [DataMember(Name = "tizen", Order = 3)] public string Tizen { get; set; }
        [DataMember(Name = "user", Order = 4)] public StashUserDto User { get; set; }
    }

    [DataContract]
    internal class StashUserDto
    {
        [DataMember(Name = "id", Order = 0)] public string Id { get; set; }
        [DataMember(Name = "name", Order = 1)] public string Name { get; set; }
    }

    [DataContract]
    internal class StatusDto
    {
        [DataMember(Name = "screen", Order = 0)] public string Screen { get; set; }
        [DataMember(Name = "version", Order = 1)] public string Version { get; set; }
    }

    [DataContract]
    internal class LoginResultDto
    {
        [DataMember(Name = "ok", Order = 0)] public bool Ok { get; set; }
        [DataMember(Name = "user", Order = 1, EmitDefaultValue = false)] public StashUserDto User { get; set; }
        [DataMember(Name = "error", Order = 2, EmitDefaultValue = false)] public string Error { get; set; }
    }

    [DataContract]
    internal class LoginRequestDto
    {
        [DataMember(Name = "login")] public string Login { get; set; }
        [DataMember(Name = "password")] public string Password { get; set; }
    }
}
