using MS_Application.DataTransferObjects.Albums;
using MS_Application.DataTransferObjects.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MS_Application.Services.Interfaces
{
    public interface IAlbumsService
    {
        Task<BaseTableResponse<AlbumResponseDto>> GetAlbums(BaseSearchDto<AlbumRequestDto> dto, long userId);
        Task<BaseTableResponse<AlbumResponseDto>> GetPublicAlbums(BaseSearchDto<AlbumRequestDto> dto);
        Task<BaseResponse<AlbumResponseDto>> GetAlbumById(long id);
        Task<BaseResponse<AlbumResponseDto>> CreateAlbum(AlbumCreateDto dto, long userId);

        Task<BaseResponse<AlbumResponseDto>> UpdateAlbum(long id, AlbumUpdateDto dto, long userId);

        Task<BaseResponse<bool>> DeleteAlbum(long id, long userId);
    }
}
