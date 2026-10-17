using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MS_Application.DataTransferObjects.User
{
    public class CrmUserRequestDto
    {
        public string? RefCode { get; set; }
        public string? Keyword { get; set; }

        public string? Username { get; set; }

        public string? Email { get; set; }

        public short? UserType { get; set; }

        public string? FullName { get; set; }

        public string? Phone { get; set; }

        public string? Gender { get; set; }

        public string? Bio { get; set; }

        public bool? IsActived { get; set; }

        public bool? IsDeleted { get; set; }

        public DateTime? FromDate { get; set; }

        public DateTime? ToDate { get; set; }

        public string? SortBy { get; set; }
    }
}
