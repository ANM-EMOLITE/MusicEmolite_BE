namespace MS_Application.DataTransferObjects.Songs
{
    public static class SongSyncStage
    {
        public const string Idle = "idle";
        public const string Fetching = "fetching";
        public const string Saving = "saving";
        public const string Lyrics = "lyrics";
        public const string Done = "done";
        public const string Failed = "failed";
    }

    public class SongSyncStatusDto
    {
        public bool IsRunning { get; set; }
        public string Stage { get; set; } = SongSyncStage.Idle;
        public DateTime? StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }

        // Số video lấy từ Youtube (đã bỏ trùng giữa các quốc gia)
        public int Fetched { get; set; }
        // Bị loại vì không phải nhạc (phim, vlog, live, quá ngắn/dài...)
        public int Filtered { get; set; }
        // Đã có trong DB
        public int Duplicated { get; set; }
        public int Inserted { get; set; }

        public int LyricsTotal { get; set; }
        public int LyricsProcessed { get; set; }
        public int LyricsFound { get; set; }

        // Bước con đang chạy, vd: "Quét kênh nghệ sĩ 35/400"
        public string? Detail { get; set; }

        public string? Message { get; set; }

        public SongSyncStatusDto Clone() => (SongSyncStatusDto)MemberwiseClone();
    }
}
