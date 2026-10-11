using MS_Application.DataTransferObjects.Songs;
using MS_Domain.Common;

namespace MS_Application.Services.SongSync
{
    /// <summary>
    /// Trạng thái lần đồng bộ hiện tại/gần nhất. Đăng ký Singleton để request
    /// gọi status (FE poll) đọc được tiến độ của lần đồng bộ đang chạy nền.
    /// Chỉ lưu trong memory: restart BE thì mất trạng thái.
    /// </summary>
    public class SongSyncState
    {
        private readonly object _lock = new();
        private SongSyncStatusDto _status = new();
        private int _keywordOffset = Random.Shared.Next(1000);

        /// <summary>
        /// Lấy `count` từ khóa kế tiếp (xoay vòng) để mỗi lần đồng bộ tìm bộ từ khóa khác nhau.
        /// </summary>
        public List<string> TakeKeywords(IReadOnlyList<string> keywords, int count)
        {
            lock (_lock)
            {
                var result = Enumerable.Range(0, Math.Min(count, keywords.Count))
                    .Select(i => keywords[(_keywordOffset + i) % keywords.Count])
                    .ToList();

                _keywordOffset = (_keywordOffset + result.Count) % keywords.Count;

                return result;
            }
        }

        public bool TryStart()
        {
            lock (_lock)
            {
                if (_status.IsRunning)
                    return false;

                _status = new SongSyncStatusDto
                {
                    IsRunning = true,
                    Stage = SongSyncStage.Fetching,
                    StartedAt = DateTimeHelper.VnNow
                };

                return true;
            }
        }

        public void Update(Action<SongSyncStatusDto> update)
        {
            lock (_lock)
            {
                update(_status);
            }
        }

        public void Finish(string? error)
        {
            lock (_lock)
            {
                _status.IsRunning = false;
                _status.FinishedAt = DateTimeHelper.VnNow;
                _status.Stage = error == null ? SongSyncStage.Done : SongSyncStage.Failed;

                if (error != null)
                    _status.Message = error;
            }
        }

        public SongSyncStatusDto Snapshot()
        {
            lock (_lock)
            {
                return _status.Clone();
            }
        }
    }
}
