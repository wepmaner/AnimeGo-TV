using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace AnimeGo.Core
{
    // JSON через DataContractJsonSerializer из BCL: сторонних пакетов в Core нет.
    internal static class Json
    {
        public static T Parse<T>(string json)
        {
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(ms);
        }

        public static string Serialize<T>(T value)
        {
            using (var ms = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(ms, value);
                var b = ms.ToArray();
                // Сериализатор в некоторых рантаймах пишет BOM — отрезаем.
                var skip = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? 3 : 0;
                return new UTF8Encoding(false).GetString(b, skip, b.Length - skip);
            }
        }
    }

    // data-user на <html>: {"id":7,"username":"x"}.
    [DataContract]
    internal class UserDto
    {
        [DataMember(Name = "id")] public int Id { get; set; }
        [DataMember(Name = "username")] public string Username { get; set; }
    }

    // Тело POST /video/progress; порядок полей совпадает с тем, что шлёт сайт.
    [DataContract]
    internal class ProgressDto
    {
        [DataMember(Name = "episodeId", Order = 0)] public int EpisodeId { get; set; }
        [DataMember(Name = "translationId", Order = 1)] public int TranslationId { get; set; }
        [DataMember(Name = "animeId", Order = 2)] public int AnimeId { get; set; }
        [DataMember(Name = "currentTime", Order = 3)] public int CurrentTime { get; set; }
        [DataMember(Name = "duration", Order = 4)] public int Duration { get; set; }
    }

    // Ответ ajax-эндпоинтов сайта: {status, message, data:{content, numVideos, isWatched}}; лишние поля игнорируются.
    [DataContract]
    internal class AjaxDto
    {
        [DataMember(Name = "status")] public string Status { get; set; }
        [DataMember(Name = "message")] public string Message { get; set; }
        [DataMember(Name = "data")] public AjaxDataDto Data { get; set; }
    }

    [DataContract]
    internal class AjaxDataDto
    {
        [DataMember(Name = "content")] public string Content { get; set; }
        [DataMember(Name = "numVideos")] public int? NumVideos { get; set; }
        [DataMember(Name = "isWatched")] public bool? IsWatched { get; set; }
    }

    // Плейлист CVH: список серий с озвучками.
    [DataContract]
    public class CvhPlaylistDto
    {
        [DataMember(Name = "titleName")] public string TitleName { get; set; }
        [DataMember(Name = "isSerial")] public bool IsSerial { get; set; }
        [DataMember(Name = "items")] public List<CvhItemDto> Items { get; set; } = new List<CvhItemDto>();
    }

    [DataContract]
    public class CvhItemDto
    {
        [DataMember(Name = "cvhId")] public string CvhId { get; set; }
        [DataMember(Name = "vkId")] public string VkId { get; set; }
        [DataMember(Name = "voiceStudio")] public string VoiceStudio { get; set; }
        [DataMember(Name = "voiceType")] public string VoiceType { get; set; }
        [DataMember(Name = "season")] public int? Season { get; set; }
        [DataMember(Name = "episode")] public int? Episode { get; set; }
    }

    [DataContract]
    internal class CvhVideoDto
    {
        [DataMember(Name = "duration")] public int? Duration { get; set; }
        [DataMember(Name = "thumbUrl")] public string ThumbUrl { get; set; }
        [DataMember(Name = "sources")] public CvhSourcesDto Sources { get; set; }
    }

    [DataContract]
    internal class CvhSourcesDto
    {
        [DataMember(Name = "hlsUrl")] public string HlsUrl { get; set; }
        [DataMember(Name = "dashUrl")] public string DashUrl { get; set; }
        [DataMember(Name = "mpegFullHdUrl")] public string MpegFullHdUrl { get; set; }
        [DataMember(Name = "mpegHighUrl")] public string MpegHighUrl { get; set; }
        [DataMember(Name = "mpegMediumUrl")] public string MpegMediumUrl { get; set; }
        [DataMember(Name = "mpegLowUrl")] public string MpegLowUrl { get; set; }
        [DataMember(Name = "mpegLowestUrl")] public string MpegLowestUrl { get; set; }
    }

    // data-parameters плеера AniBoom: hls и dash — вложенные JSON-строки.
    [DataContract]
    internal class AniboomParamsDto
    {
        [DataMember(Name = "hls")] public string Hls { get; set; }
        [DataMember(Name = "dash")] public string Dash { get; set; }
        [DataMember(Name = "duration")] public int? Duration { get; set; }
        [DataMember(Name = "poster")] public string Poster { get; set; }
    }

    [DataContract]
    internal class AniboomSrcDto
    {
        [DataMember(Name = "src")] public string Src { get; set; }
        [DataMember(Name = "type")] public string Type { get; set; }
    }
}
