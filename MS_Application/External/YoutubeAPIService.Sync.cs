using System.Net;
using System.Text.Json;
using MS_Application.DataTransferObjects.Youtube;

namespace MS_Application.External;

/// <summary>
/// Các API Youtube dùng cho đồng bộ nhạc (SongSyncService). Quota mỗi lần gọi:
/// i18nRegions / videos.list / channels.list / playlistItems.list = 1 unit,
/// search.list = 100 unit (và bị giới hạn ~100 request/ngày).
/// </summary>
public partial class YoutubeAPIService
{
    private const string YoutubeApiBase = "https://www.googleapis.com/youtube/v3";

    public async Task<YoutubeApiPageDto<string>> GetRegionCodesAsync()
    {
        return await GetPageAsync(
            $"{YoutubeApiBase}/i18nRegions?part=snippet",
            item => item.TryGetProperty("snippet", out var snippet)
                && snippet.TryGetProperty("gl", out var gl)
                    ? gl.GetString()
                    : null);
    }

    /// <summary>
    /// Một trang chart mostPopular, category Music (10), theo quốc gia.
    /// </summary>
    public async Task<YoutubeApiPageDto<YoutubeVideoDto>> GetMostPopularMusicAsync(string regionCode, string? pageToken)
    {
        return await GetPageAsync(
            $"{YoutubeApiBase}/videos?part=snippet,contentDetails,statistics,status,topicDetails" +
            $"&chart=mostPopular&videoCategoryId=10" +
            $"&regionCode={Uri.EscapeDataString(regionCode)}" +
            $"&maxResults={YoutubePageSize}" +
            PageTokenParam(pageToken),
            ParseVideoItem);
    }

    /// <summary>
    /// Chi tiết (duration/status/category...) cho tối đa 50 video.
    /// </summary>
    public async Task<YoutubeApiPageDto<YoutubeVideoDto>> GetVideosByIdsAsync(IEnumerable<string> videoIds)
    {
        return await GetPageAsync(
            $"{YoutubeApiBase}/videos?part=snippet,contentDetails,statistics,status,topicDetails" +
            $"&id={string.Join(",", videoIds)}",
            ParseVideoItem);
    }

    /// <summary>
    /// search.list giới hạn trong category Music, chỉ trả về videoId.
    /// </summary>
    public async Task<YoutubeApiPageDto<string>> SearchMusicVideoIdsAsync(string keyword, string? pageToken)
    {
        return await GetPageAsync(
            $"{YoutubeApiBase}/search?part=id&type=video&videoCategoryId=10" +
            $"&maxResults={YoutubePageSize}" +
            $"&q={Uri.EscapeDataString(keyword)}" +
            PageTokenParam(pageToken),
            item => item.TryGetProperty("id", out var id)
                && id.TryGetProperty("videoId", out var videoId)
                    ? videoId.GetString()
                    : null);
    }

    /// <summary>
    /// Playlist "uploads" (toàn bộ video đã đăng) + chủ đề của tối đa 50 kênh.
    /// </summary>
    public async Task<YoutubeApiPageDto<YoutubeChannelUploadsDto>> GetUploadsPlaylistIdsAsync(IEnumerable<string> channelIds)
    {
        return await GetPageAsync(
            $"{YoutubeApiBase}/channels?part=contentDetails,topicDetails" +
            $"&id={string.Join(",", channelIds)}&maxResults={YoutubePageSize}",
            item =>
            {
                var channelId = item.GetProperty("id").GetString();
                var uploads = item.TryGetProperty("contentDetails", out var details)
                    && details.TryGetProperty("relatedPlaylists", out var related)
                    && related.TryGetProperty("uploads", out var up)
                        ? up.GetString()
                        : null;

                return string.IsNullOrEmpty(channelId) || string.IsNullOrEmpty(uploads)
                    ? null
                    : new YoutubeChannelUploadsDto
                    {
                        ChannelId = channelId,
                        PlaylistId = uploads,
                        TopicCategories = ParseTopicCategories(item)
                    };
            });
    }

    public async Task<YoutubeApiPageDto<string>> GetPlaylistVideoIdsAsync(string playlistId, string? pageToken)
    {
        return await GetPageAsync(
            $"{YoutubeApiBase}/playlistItems?part=contentDetails" +
            $"&playlistId={Uri.EscapeDataString(playlistId)}" +
            $"&maxResults={YoutubePageSize}" +
            PageTokenParam(pageToken),
            item => item.TryGetProperty("contentDetails", out var details)
                && details.TryGetProperty("videoId", out var videoId)
                    ? videoId.GetString()
                    : null);
    }

    private static string PageTokenParam(string? pageToken)
        => string.IsNullOrEmpty(pageToken) ? "" : $"&pageToken={Uri.EscapeDataString(pageToken)}";

    private async Task<YoutubeApiPageDto<T>> GetPageAsync<T>(string url, Func<JsonElement, T?> parseItem)
    {
        var result = new YoutubeApiPageDto<T>();

        var response = await _httpClient.GetAsync($"{url}&key={_configuration["Youtube:ApiKey"]}");
        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            // Youtube báo hết quota bằng 403 (reason=quotaExceeded) hoặc 429
            result.QuotaExceeded =
                response.StatusCode == HttpStatusCode.TooManyRequests
                || (response.StatusCode == HttpStatusCode.Forbidden && json.Contains("quotaExceeded"));
            result.Error = ((int)response.StatusCode).ToString();

            return result;
        }

        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("items", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                var parsed = parseItem(item);
                if (parsed != null)
                    result.Items.Add(parsed);
            }
        }

        result.NextPageToken = doc.RootElement.TryGetProperty("nextPageToken", out var npt)
            ? npt.GetString()
            : null;

        return result;
    }

    /// <summary>
    /// topicDetails.topicCategories là URL Wikipedia ("https://en.wikipedia.org/wiki/Pop_music"),
    /// chỉ giữ phần tên chủ đề ("Pop_music").
    /// </summary>
    private static List<string> ParseTopicCategories(JsonElement item)
    {
        var result = new List<string>();

        if (!item.TryGetProperty("topicDetails", out var topicDetails)
            || !topicDetails.TryGetProperty("topicCategories", out var categories)
            || categories.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var category in categories.EnumerateArray())
        {
            var url = category.GetString();
            if (!string.IsNullOrEmpty(url))
                result.Add(url[(url.LastIndexOf('/') + 1)..]);
        }

        return result;
    }

    private static YoutubeVideoDto? ParseVideoItem(JsonElement item)
    {
        var videoId = item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString()
            : null;

        if (string.IsNullOrEmpty(videoId) || !item.TryGetProperty("snippet", out var snippet))
            return null;

        var thumbnails = snippet.TryGetProperty("thumbnails", out var thumbs) ? thumbs : default;

        var video = new YoutubeVideoDto
        {
            VideoId = videoId,
            Title = snippet.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
            ChannelId = snippet.TryGetProperty("channelId", out var channelId) ? channelId.GetString() ?? "" : "",
            Channel = snippet.TryGetProperty("channelTitle", out var channel) ? channel.GetString() ?? "" : "",
            CategoryId = snippet.TryGetProperty("categoryId", out var categoryId) ? categoryId.GetString() ?? "" : "",
            PublishedAt = snippet.TryGetProperty("publishedAt", out var publishedAt)
                && publishedAt.TryGetDateTime(out var parsedPublishedAt)
                    ? parsedPublishedAt
                    : null,
            LiveBroadcastContent = snippet.TryGetProperty("liveBroadcastContent", out var live) ? live.GetString() ?? "" : "",
            ThumbnailMedium = thumbnails.ValueKind == JsonValueKind.Object ? GetThumbnailUrl(thumbnails, "medium") : "",
            ThumbnailHigh = thumbnails.ValueKind == JsonValueKind.Object ? GetThumbnailUrl(thumbnails, "high") : "",
            ThumbnailMaxres = thumbnails.ValueKind == JsonValueKind.Object ? GetThumbnailUrl(thumbnails, "maxres") : "",
            TopicCategories = ParseTopicCategories(item)
        };

        if (item.TryGetProperty("contentDetails", out var contentDetails)
            && contentDetails.TryGetProperty("duration", out var duration))
        {
            video.DurationRaw = duration.GetString() ?? "";
            video.Duration = ParseYoutubeDurationToSeconds(video.DurationRaw);
        }

        if (item.TryGetProperty("statistics", out var statistics)
            && statistics.TryGetProperty("viewCount", out var viewCount)
            && long.TryParse(viewCount.GetString(), out var parsedViews))
        {
            video.Views = parsedViews;
        }

        if (item.TryGetProperty("status", out var status))
        {
            video.Embeddable = status.TryGetProperty("embeddable", out var embeddable)
                && embeddable.ValueKind == JsonValueKind.True;
            video.PrivacyStatus = status.TryGetProperty("privacyStatus", out var privacyStatus)
                ? privacyStatus.GetString() ?? ""
                : "";
        }

        return video;
    }
}
