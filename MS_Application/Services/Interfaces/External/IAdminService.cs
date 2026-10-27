using MS_Application.DataTransferObjects.ActivityLog;
using MS_Application.DataTransferObjects.Albums;
using MS_Application.DataTransferObjects.Artist;
using MS_Application.DataTransferObjects.Bank;
using MS_Application.DataTransferObjects.Base;
using MS_Application.DataTransferObjects.Dashboard;
using MS_Application.DataTransferObjects.Lyrics;
using MS_Application.DataTransferObjects.Songs;
using MS_Application.DataTransferObjects.User;
using MS_Application.DataTransferObjects.User.BankUser;
using MS_Domain.Common;

namespace MS_Application.Services.Interfaces.External
{
    public interface IAdminService
    {
        Task<BaseResponse<DashboardSummaryResponseDto>> GetDashboardSummary();

        Task<BaseResponse<List<DashboardTrendResponseDto>>> GetDashboardTrend();

        Task<BaseTableResponse<ActivityLogResponseDto>> SearchActivityLogs(BaseSearchDto<ActivityLogSearchRequest> dto);

        Task<BaseTableResponse<SongResponseDto>> SearchSongs(BaseSearchDto<SongRequestDto> dto, long userId);

        Task<BaseResponse<SongResponseDto>> CreateSong(SongCreateDto dto, long userId);

        Task<BaseTableResponse<SongResponseDto>> GetLikedSongsByUser(long userId, BaseSearchDto<SongRequestDto> dto);

        Task<BaseTableResponse<AlbumResponseDto>> SearchAlbums(BaseSearchDto<AlbumRequestDto> dto);

        Task<BaseTableResponse<AlbumResponseDto>> GetAlbumsByUser(long userId, BaseSearchDto<AlbumRequestDto> dto);

        Task<BaseTableResponse<ArtistResponseDto>> SearchArtists(BaseSearchDto<ArtistRequestDto> dto);

        Task<BaseTableResponse<CrmUserResponseDto>> SearchUsers(BaseSearchDto<CrmUserRequestDto> dto);

        Task<BaseResponse<CrmUserProfileResponseDto>> GetProfile(long userId);

        Task<BaseResponse<bool>> UpdateProfile(long userId, UpdateUserRequestDto dto);

        Task<BaseResponse<BankUserResponseDto>> GetBankUser(long userId);

        Task<BaseResponse<BankUserResponseDto>> CreateBankUser(long userId, string refCode, BankUserRequestDto dto);

        Task<BaseResponse<BankUserResponseDto>> UpdateBankUser(long userId, long id, BankUserRequestDto dto);

        Task<BaseTableResponse<BankResponseDto>> GetBanks();

        BaseResponse<SongSyncStatusDto> StartSongSync(long userId);

        BaseResponse<SongSyncStatusDto> GetSongSyncStatus();

        Task<BaseResponse<LyricsResponseDto?>> GetLyricsById(int id);

        Task<BaseTableResponse<LyricsResponseDto>> SearchLyrics(BaseSearchDto<LyricsSearchRequestDto> request);

        Task<BaseResponse<bool>> PublishLyrics(PublishLyricsRequestDto request);
    }
}
