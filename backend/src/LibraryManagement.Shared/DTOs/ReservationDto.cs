using LibraryManagement.Shared.Enums;

namespace LibraryManagement.Shared.DTOs;

public class ReservationDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string StudentCode { get; set; } = string.Empty;
    public int BookId { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public string BookAuthor { get; set; } = string.Empty;
    public int? BookCopyId { get; set; }
    public string? CopyBarcode { get; set; }
    public ReservationStatus Status { get; set; }
    public string StatusText => Status.ToString();
    public string? Notes { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
