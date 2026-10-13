namespace MS_Application.DataTransferObjects.Youtube
{
    /// <summary>
    /// Kết quả 1 lần gọi Youtube API (1 trang) dùng cho đồng bộ nhạc.
    /// </summary>
    public class YoutubeApiPageDto<T>
    {
        public List<T> Items { get; set; } = new();
        public string? NextPageToken { get; set; }
        public bool QuotaExceeded { get; set; }
        public string? Error { get; set; }
    }
}

namespace MS_Application.DataTransferObjects.Youtube
{
    public class YoutubeChannelUploadsDto
    {
        public string ChannelId { get; set; } = "";
        // Playlist "uploads" chứa toàn bộ video kênh đã đăng
        public string PlaylistId { get; set; } = "";
        // Chủ đề Youtube tự gán cho kênh, vd "Music", "Pop_music", "Lifestyle_(sociology)"
        public List<string> TopicCategories { get; set; } = new();
    }
}
