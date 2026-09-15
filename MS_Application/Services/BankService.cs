using MS_Application.DataTransferObjects.Bank;
using MS_Application.DataTransferObjects.Base;
using MS_Application.Services.Interfaces;
using MS_Application.Services.Interfaces.External;
using System.Net.Http.Json;

namespace MS_Application.Services
{
    public class BankService : IBankService
    {
        public BankService() { }
        public async Task<BaseTableResponse<BankResponseDto>> GetBanks()
        {
            using var client = new HttpClient();
            var response = await client.GetFromJsonAsync<VietQrResponse>("https://api.vietqr.io/v2/banks");

            return new BaseTableResponse<BankResponseDto>
            {
                Data = response?.Data?
                    .Select(x => new BankResponseDto
                    {
                        Code = x.Code,
                        Bin = x.Bin,
                        Name = x.Name,
                        ShortName = x.ShortName,
                        Logo = x.Logo
                    })
                    .ToList()
            };
        }
    }
}
