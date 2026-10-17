using MS_Application.DataTransferObjects.Base;

namespace MS_Application.DataTransferObjects.User
{
    public class CrmUserResponseDto : BaseDto
    {
        public string? RefCode { get; set; }
        public string? Username { get; set; }
        public string? Email { get; set; }
        public short UserType { get; set; }

        public string? UserTypeName { get; set; }
        public string? FullName { get; set; }

        public string? Phone { get; set; }

        public DateOnly? DateOfBirth { get; set; }

        public string? Gender { get; set; }

        public string? Bio { get; set; }

        public string? Uri { get; set; }

        public bool? IsActived { get; set; }

        public bool? IsDeleted { get; set; }
    }
}
