using MS_Application.DataTransferObjects.Base;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MS_Domain.Entities.DISTS
{
    [Table("songs", Schema = "dist")]
    public class DistSongs : BaseModel
    {
        [Column("title")]
        public string Title { get; set; }
        [Column("duration")]
        public int Duration { get; set; }
        [Column("release_date")]
        public DateOnly ReleaseDate { get; set; }
        [Column("album_id")]
        public long AlbumId { get; set; }
        [Column("file_url")]
        public string? FileUrl { get; set; }
        [Column("img_url")]
        public string? ImgUrl { get; set; }
        [Column("artist_id")]
        public long? ArtistId { get; set; }
        [Column("views")]
        public long? Views { get; set; } = 0;
        [Column("likes")]
        public long? Likes { get; set; } = 0;
        [Column("type")]
        public short Type { get; set; }
        [Column("youtube_video_id")]
        public string? YoutubeVideoId { get; set; }

        [Column("play_count")]
        public long? PlayCount { get; set; }
        [Column("source_type")]
        public short SourceType { get; set; }

        // Do trigger DB tự tính (tên bài + ca sĩ, chữ thường, không dấu) - EF không ghi cột này
        [Column("search_text")]
        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        public string? SearchText { get; set; }
        public DistArtists Artist { get; set; }
    }
}
