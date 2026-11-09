using LibraryManagement.Shared.Enums;

namespace LibraryManagement.Infrastructure.Entities;

public class BookCopy
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public Book Book { get; set; } = null!;
    public string Barcode { get; set; } = string.Empty;
    public BookCopyStatus Status { get; set; } = BookCopyStatus.Available;
    public int? CurrentReservationId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
