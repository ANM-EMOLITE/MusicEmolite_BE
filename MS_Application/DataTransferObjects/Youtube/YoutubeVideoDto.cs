using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MS_Application.DataTransferObjects.Youtube
{
    public class YoutubeVideoDto
    {
        public string VideoId { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Etag { get; set; } = "";

        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string ChannelId { get; set; } = "";
        public string Channel { get; set; } = "";
        public string ChannelThumbnail { get; set; } = "";
        public string CategoryId { get; set; } = "";
        // Chủ đề Youtube tự gán cho video (topicDetails), chỉ có ở luồng đồng bộ nhạc
        public List<string> TopicCategories { get; set; } = new();
        public DateTime? PublishedAt { get; set; }
        public bool IsLiked { get; set; }
        public long? SongId { get; set; }
        public List<long> AlbumIds { get; set; } = new();

        public string ThumbnailDefault { get; set; } = "";
        public string ThumbnailMedium { get; set; } = "";
        public string ThumbnailHigh { get; set; } = "";
        public string ThumbnailStandard { get; set; } = "";
        public string ThumbnailMaxres { get; set; } = "";

        public string LiveBroadcastContent { get; set; } = "";
        public string PublishTime { get; set; } = "";

        public string DurationRaw { get; set; } = "";
        public int Duration { get; set; }

        public string Dimension { get; set; } = "";
        public string Definition { get; set; } = "";
        public bool Caption { get; set; }
        public bool LicensedContent { get; set; }
        public string Projection { get; set; } = "";

        public long Views { get; set; }
        public long LikeCount { get; set; }
        public long CommentCount { get; set; }

        public bool Embeddable { get; set; }
        public bool PublicStatsViewable { get; set; }
        public string PrivacyStatus { get; set; } = "";
        public string UploadStatus { get; set; } = "";

        public string EmbedHtml { get; set; } = "";
    }
}
