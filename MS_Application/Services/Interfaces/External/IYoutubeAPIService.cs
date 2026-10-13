using MS_Application.DataTransferObjects.Base;
using MS_Application.DataTransferObjects.Youtube;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MS_Application.Services.Interfaces.External
{
    public interface IYoutubeAPIService
    {
        Task<BaseTableResponse<YoutubeVideoDto>> SearchAsync(BaseSearchDto<YoutubeSearchRequestDto> request, long userId);

        // Dùng cho đồng bộ nhạc
        Task<YoutubeApiPageDto<string>> GetRegionCodesAsync();
        Task<YoutubeApiPageDto<YoutubeVideoDto>> GetMostPopularMusicAsync(string regionCode, string? pageToken);
        Task<YoutubeApiPageDto<YoutubeVideoDto>> GetVideosByIdsAsync(IEnumerable<string> videoIds);
        Task<YoutubeApiPageDto<string>> SearchMusicVideoIdsAsync(string keyword, string? pageToken);
        Task<YoutubeApiPageDto<YoutubeChannelUploadsDto>> GetUploadsPlaylistIdsAsync(IEnumerable<string> channelIds);
        Task<YoutubeApiPageDto<string>> GetPlaylistVideoIdsAsync(string playlistId, string? pageToken);
    }
}
