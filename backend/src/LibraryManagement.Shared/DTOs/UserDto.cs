namespace LibraryManagement.Shared.DTOs;

public class UserDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
}
