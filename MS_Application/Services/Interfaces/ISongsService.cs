using Microsoft.AspNetCore.Mvc;
using MS_Application.DataTransferObjects.Base;
using MS_Application.DataTransferObjects.Songs;

namespace MS_Application.Services.Interfaces
{
    public interface ISongsService
    {
        Task<BaseTableResponse<SongResponseDto>> GetSongs(BaseSearchDto<SongRequestDto> dto, long userId);
        Task<BaseTableResponse<SongResponseDto>> GetPublicSongs(BaseSearchDto<SongRequestDto> dto, long userId);
        Task<BaseTableResponse<SongResponseDto>> GetRecentSongs(BaseSearchDto<SongRequestDto> dto, long userId);
        Task<BaseTableResponse<SongResponseDto>> GetTrendingSongs(BaseSearchDto<SongRequestDto> dto, long userId);
        Task<BaseTableResponse<SongResponseDto>> GetNewestSongs(BaseSearchDto<SongRequestDto> dto, long userId);
        Task<BaseTableResponse<SongResponseDto>> GetMostPlayedSongs(long userId, int top = 20);
        Task<BaseResponse<MixPlaylistResponseDto>> GetMostPlayedMix(long userId, int top = 20);
        Task<BaseResponse<SongResponseDto>> GetSongDetail(long id, long userId);
        Task<BaseResponse<SongResponseDto>> IncrementView(long id, long userId);
        Task<BaseResponse<SongResponseDto>> AddSongHistory(AddSongHistoryDto dto, long userId);
        Task<BaseResponse<SongResponseDto>> ToggleLike(long id, long userId);
        Task<BaseResponse<SongResponseDto>> CreateSong(SongCreateDto dto, long userId);
        Task<BaseResponse<SongResponseDto>> AddSongToAlbum(long songId, long albumId, long userId);
        Task<BaseResponse<bool>> RemoveSongFromAlbum(long songId, long albumId, long userId);
    }
}
