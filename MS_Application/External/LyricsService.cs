using Microsoft.Extensions.Options;
using MS_Application.Constants;
using MS_Application.DataTransferObjects.Base;
using MS_Application.DataTransferObjects.Lyrics;
using MS_Application.Helpers;
using MS_Application.Services.Interfaces.External;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MS_Application.Services;

public class LyricsService : ILyricsService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LyricsApiSettingsDto _settings;

    public LyricsService(
        IHttpClientFactory httpClientFactory,
        IOptions<LyricsApiSettingsDto> settings)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
    }

    public async Task<BaseResponse<LyricsResponseDto?>> GetLyricsAsync(LyricsRequestDto request)
    {
        var result = new BaseResponse<LyricsResponseDto?>();
        var client = _httpClientFactory.CreateClient();

        var query = new List<string>();

        if (!string.IsNullOrWhiteSpace(request.Title))
            query.Add($"track_name={Uri.EscapeDataString(request.Title)}");

        if (!string.IsNullOrWhiteSpace(request.Artist))
            query.Add($"artist_name={Uri.EscapeDataString(request.Artist)}");

        var url = $"{_settings.Url}/get?{string.Join("&", query)}";

        Console.WriteLine(url);

        var response = await client.GetAsync(url);
        var json = await response.Content.ReadAsStringAsync();

        Console.WriteLine(json);

        if (!response.IsSuccessStatusCode)
        {
            result.Data = null;
            result.Code = ResponseStatusCode.Status404;
            return result.Fail(string.Format(Messages.Validation.NotFound, "Lyrics"));
        }

        using var doc = JsonDocument.Parse(json);

        var root = doc.RootElement;

        var data = new LyricsResponseDto
        {
            Id = root.TryGetProperty("id", out var id)
        && id.ValueKind == JsonValueKind.Number
        && id.TryGetInt32(out var parsedId)
            ? parsedId
            : 0,

            Name = root.TryGetProperty("name", out var name)
        ? name.GetString() ?? ""
        : "",

            Title = root.TryGetProperty("trackName", out var title)
        ? title.GetString() ?? ""
        : "",

            Artist = root.TryGetProperty("artistName", out var artist)
        ? artist.GetString() ?? ""
        : "",

            Album = root.TryGetProperty("albumName", out var album)
        ? album.GetString() ?? ""
        : "",

            Duration = root.TryGetProperty("duration", out var duration)
        && duration.ValueKind == JsonValueKind.Number
        && duration.TryGetInt32(out var parsedDuration)
            ? parsedDuration
            : 0,

            Instrumental = root.TryGetProperty("instrumental", out var instrumental)
        && instrumental.ValueKind == JsonValueKind.True
            ? true
            : false,

            Lyrics = root.TryGetProperty("plainLyrics", out var lyrics)
        ? lyrics.GetString() ?? ""
        : "",

            RawSyncedLyrics = root.TryGetProperty("syncedLyrics", out var raw)
        ? raw.GetString() ?? ""
        : "",

            SyncedLyrics = root.TryGetProperty("syncedLyrics", out var synced)
        ? ParseLrc(synced.GetString() ?? "")
        : []
        };
        result.Data = data;
        return result.Success(string.Format(Messages.Action.GetSuccess, "lyrics"));
    }

    public async Task<BaseTableResponse<LyricsResponseDto>> SearchLyricsAsync(BaseSearchDto<LyricsSearchRequestDto> request)
    {
        var result = new BaseTableResponse<LyricsResponseDto>();
        var client = _httpClientFactory.CreateClient();
        var search = request.SearchParams;

        if (search == null)
        {
            result.Code = ResponseStatusCode.Status400;
            result.Type = GlobalConstants.ResponseType.Error;
            result.Message = "Search params is required";

            return result;
        }

        var query = new List<string>();

        if (!string.IsNullOrWhiteSpace(search.Query))
            query.Add($"q={Uri.EscapeDataString(search.Query)}");

        if (!string.IsNullOrWhiteSpace(search.Title))
            query.Add($"track_name={Uri.EscapeDataString(search.Title)}");

        if (!string.IsNullOrWhiteSpace(search.Artist))
            query.Add($"artist_name={Uri.EscapeDataString(search.Artist)}");

        if (!string.IsNullOrWhiteSpace(search.Album))
            query.Add($"album_name={Uri.EscapeDataString(search.Album)}");

        if (string.IsNullOrWhiteSpace(search.Query)
            && string.IsNullOrWhiteSpace(search.Title))
        {
            result.Code = ResponseStatusCode.Status400;
            result.Type = GlobalConstants.ResponseType.Error;
            result.Message = "Query or track name is required";

            return result;
        }

        var url = $"{_settings.Url}/search?{string.Join("&", query)}";

        Console.WriteLine(url);

        var response = await client.GetAsync(url);

        var json = await response.Content.ReadAsStringAsync();

        Console.WriteLine(json);

        if (!response.IsSuccessStatusCode)
        {
            result.Code = ResponseStatusCode.Status404;
            result.Type = GlobalConstants.ResponseType.Error;
            result.Message = string.Format(Messages.Validation.NotFound, "Lyrics");

            return result;
        }

        using var doc = JsonDocument.Parse(json);

        var data = new List<LyricsResponseDto>();

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            data.Add(new LyricsResponseDto
            {
                Id = item.TryGetProperty("id", out var id)
                    && id.TryGetInt32(out var parsedId)
                        ? parsedId
                        : 0,

                Title = item.TryGetProperty("trackName", out var title)
                    ? title.GetString() ?? ""
                    : "",

                Artist = item.TryGetProperty("artistName", out var artist)
                    ? artist.GetString() ?? ""
                    : "",

                Album = item.TryGetProperty("albumName", out var album)
                    ? album.GetString() ?? ""
                    : "",

                Duration = item.TryGetProperty("duration", out var duration)
                    && duration.TryGetInt32(out var parsedDuration)
                        ? parsedDuration
                        : 0,

                Instrumental =
                    item.TryGetProperty("instrumental", out var instrumental)
                    && instrumental.ValueKind == JsonValueKind.True,

                Lyrics = item.TryGetProperty("plainLyrics", out var lyrics)
                    ? lyrics.GetString() ?? ""
                    : "",

                RawSyncedLyrics = item.TryGetProperty("syncedLyrics", out var raw)
                    ? raw.GetString() ?? ""
                    : "",

                SyncedLyrics = item.TryGetProperty("syncedLyrics", out var synced)
                    ? ParseLrc(synced.GetString() ?? "")
                    : []
            });
        }

        var totalRecords = data.Count;

        data = request.Asc
            ? data.OrderBy(x => x.Title).ToList()
            : data.OrderByDescending(x => x.Title).ToList();

        data = data
            .Skip(request.Start)
            .Take(request.PageSize)
            .ToList();

        result.Code = ResponseStatusCode.Status200;
        result.Type = GlobalConstants.ResponseType.Success;
        result.Message = string.Format(Messages.Action.GetSuccess, "lyrics");

        result.TotalRecords = totalRecords;

        result.TotalPages = (int)Math.Ceiling(
            totalRecords / (double)request.PageSize);

        result.Data = data;

        return result;
    }

    public async Task<BaseResponse<LyricsResponseDto?>> GetLyricsByIdAsync(int id)
    {
        var result = new BaseResponse<LyricsResponseDto?>();

        var client = _httpClientFactory.CreateClient();

        var url = $"{_settings.Url}/get/{id}";

        Console.WriteLine(url);

        var response = await client.GetAsync(url);

        var json = await response.Content.ReadAsStringAsync();

        Console.WriteLine(json);

        if (!response.IsSuccessStatusCode)
        {
            result.Data = null;

            result.Code = ResponseStatusCode.Status404;

            return result.Fail(string.Format(Messages.Validation.NotFound, "Lyrics"));
        }

        using var doc = JsonDocument.Parse(json);

        var root = doc.RootElement;

        var data = new LyricsResponseDto
        {
            Id = root.TryGetProperty("id", out var lyricId)
                && lyricId.TryGetInt32(out var parsedId)
                    ? parsedId
                    : 0,

            Title = root.TryGetProperty("trackName", out var title)
                ? title.GetString() ?? ""
                : "",

            Artist = root.TryGetProperty("artistName", out var artist)
                ? artist.GetString() ?? ""
                : "",

            Album = root.TryGetProperty("albumName", out var album)
                ? album.GetString() ?? ""
                : "",

            Duration = root.TryGetProperty("duration", out var duration)
                && duration.TryGetInt32(out var parsedDuration)
                    ? parsedDuration
                    : 0,

            Instrumental =
                root.TryGetProperty("instrumental", out var instrumental)
                && instrumental.ValueKind == JsonValueKind.True,

            Lyrics = root.TryGetProperty("plainLyrics", out var lyrics)
                ? lyrics.GetString() ?? ""
                : "",

            RawSyncedLyrics = root.TryGetProperty("syncedLyrics", out var raw)
                ? raw.GetString() ?? ""
                : "",

            SyncedLyrics = root.TryGetProperty("syncedLyrics", out var synced)
                ? ParseLrc(synced.GetString() ?? "")
                : []
        };

        result.Data = data;

        return result.Success(
            string.Format(Messages.Action.GetSuccess, "lyrics"));
    }

    public async Task<BaseResponse<bool>> PublishLyricsAsync(PublishLyricsRequestDto request)
    {
        var result = new BaseResponse<bool>();

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MyApp/1.0)");

        var challengeResponse = await client.PostAsync(
            $"{_settings.Url}/request-challenge", null);

        Console.WriteLine($"Challenge status: {challengeResponse.StatusCode}");

        if (!challengeResponse.IsSuccessStatusCode)
            return result.Fail("Không lấy được challenge");

        var challengeJson = await challengeResponse.Content.ReadAsStringAsync();
        Console.WriteLine($"Challenge JSON: {challengeJson}");

        using var challengeDoc = JsonDocument.Parse(challengeJson);
        var prefix = challengeDoc.RootElement.GetProperty("prefix").GetString();
        var target = challengeDoc.RootElement.GetProperty("target").GetString();

        if (string.IsNullOrWhiteSpace(prefix) || string.IsNullOrWhiteSpace(target))
            return result.Fail("Challenge không hợp lệ");

        var token = await GeneratePublishToken(prefix, target);

        if (string.IsNullOrWhiteSpace(token))
            return result.Fail("Không tạo được publish token");

        Console.WriteLine($"TOKEN = {token}");

        var payload = new
        {
            trackName = request.TrackName,
            artistName = request.ArtistName,
            albumName = request.AlbumName,
            duration = request.Duration,
            plainLyrics = request.PlainLyrics ?? "",
            syncedLyrics = request.SyncedLyrics ?? ""
        };

        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        Console.WriteLine($"Payload: {payloadJson}");

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_settings.Url}/publish");
        httpRequest.Headers.TryAddWithoutValidation("X-Publish-Token", token);
        httpRequest.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

        var publishResponse = await client.SendAsync(httpRequest);
        var publishJson = await publishResponse.Content.ReadAsStringAsync();

        Console.WriteLine($"Publish status: {publishResponse.StatusCode}");
        Console.WriteLine($"Publish response: {publishJson}");

        if (!publishResponse.IsSuccessStatusCode)
            return result.Fail($"Publish lyrics thất bại: {publishJson}");

        result.Data = true;
        return result.Success("Publish lyrics thành công");
    }

    private async Task<string?> GeneratePublishToken(string prefix, string target)
    {
        return await Task.Run(() =>
        {
            var targetBytes = Convert.FromHexString(target);
            long nonce = 0;

            while (true)
            {
                var input = $"{prefix}{nonce}";
                var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));

                if (IsHashLessThanOrEqual(hashBytes, targetBytes))
                {
                    var token = $"{prefix}:{nonce}";
                    Console.WriteLine($"FOUND TOKEN: {token}");
                    Console.WriteLine($"HASH: {Convert.ToHexString(hashBytes)}");
                    return token;
                }

                nonce++;
            }
        });
    }

    private static bool IsHashLessThanOrEqual(byte[] hash, byte[] target)
    {
        if (hash.Length != target.Length)
            return false;

        for (int i = 0; i < hash.Length - 1; i++)
        {
            if (hash[i] > target[i]) return false;
            if (hash[i] < target[i]) return true;
        }

        return true;
    }

    internal static List<LyricsLineDto> ParseLrc(string lrc)
    {
        var result = new List<LyricsLineDto>();

        if (string.IsNullOrWhiteSpace(lrc))
            return result;

        var lines = lrc.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var match = Regex.Match(line, @"\[(\d+):(\d+\.\d+)\](.*)");

            if (!match.Success)
                continue;

            var minutes = int.Parse(match.Groups[1].Value);

            var seconds = double.Parse(
                match.Groups[2].Value,
                CultureInfo.InvariantCulture
            );

            var text = match.Groups[3].Value.Trim();

            // bỏ line rỗng
            if (string.IsNullOrWhiteSpace(text))
                continue;

            result.Add(new LyricsLineDto
            {
                Time = (minutes * 60) + seconds,
                Text = text
            });
        }

        return result;
    }
}