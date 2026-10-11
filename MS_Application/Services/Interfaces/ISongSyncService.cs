using MS_Application.DataTransferObjects.Base;
using MS_Application.DataTransferObjects.Songs;

namespace MS_Application.Services.Interfaces
{
    public interface ISongSyncService
    {
        BaseResponse<SongSyncStatusDto> Start(long userId);

        BaseResponse<SongSyncStatusDto> GetStatus();
    }
}
