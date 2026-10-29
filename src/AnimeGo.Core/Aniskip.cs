using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;

namespace AnimeGo.Core
{
    // Интервал в секундах серии: [StartSec, EndSec).
    public sealed class SkipInterval
    {
        public int StartSec;
        public int EndSec;
    }

    // aniskip.com: тайминги опенинга по MAL id (= titleId CVH) и номеру серии. Чистая часть, без сети.
    // Покрытие неполное, данных нет — null, и плеер пропускает опенинг по-старому (API-findings.md).
    public static class Aniskip
    {
        const int MaxShiftSec = 60;

        public static string Url(int malId, int episode) =>
            "https://api.aniskip.com/v2/skip-times/" + malId.ToString(CultureInfo.InvariantCulture) + "/" +
            episode.ToString(CultureInfo.InvariantCulture) + "?types=op&types=ed&episodeLength=0";

        // Тайминги сняты с релиза длиной episodeLength; у озвучки бывает вставка в начале, поэтому сдвигаем на
        // durationSec − episodeLength, если сдвиг 0–60 с; иначе релиз другой и таймингам не верим. Не бросает.
        public static SkipInterval ParseOpening(string json, int durationSec)
        {
            if (durationSec <= 0) return null;
            AniskipDto dto;
            try { dto = Json.Parse<AniskipDto>(json ?? ""); }
            catch (Exception) { return null; }   // тип ошибки разбора зависит от рантайма
            var op = dto?.Found == true ? dto.Results?.FirstOrDefault(r => r?.SkipType == "op" && r.Interval != null) : null;
            if (op == null || op.EpisodeLength <= 0) return null;
            var shift = durationSec - op.EpisodeLength;
            if (shift < -1 || shift > MaxShiftSec) return null;   // −1: длительности округлены по-разному
            shift = Math.Max(0, shift);
            var start = (int)Math.Round(op.Interval.StartTime + shift);
            var end = (int)Math.Round(op.Interval.EndTime + shift);
            if (start < 0 || end <= start || end > durationSec) return null;
            return new SkipInterval { StartSec = start, EndSec = end };
        }
    }

    [DataContract]
    internal class AniskipDto
    {
        [DataMember(Name = "found")] public bool Found { get; set; }
        [DataMember(Name = "results")] public List<AniskipResultDto> Results { get; set; }
    }

    [DataContract]
    internal class AniskipResultDto
    {
        [DataMember(Name = "skipType")] public string SkipType { get; set; }
        [DataMember(Name = "interval")] public AniskipIntervalDto Interval { get; set; }
        [DataMember(Name = "episodeLength")] public double EpisodeLength { get; set; }
    }

    [DataContract]
    internal class AniskipIntervalDto
    {
        [DataMember(Name = "startTime")] public double StartTime { get; set; }
        [DataMember(Name = "endTime")] public double EndTime { get; set; }
    }
}
