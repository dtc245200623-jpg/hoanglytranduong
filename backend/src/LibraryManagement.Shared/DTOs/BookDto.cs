using LibraryManagement.Shared.Enums;

namespace LibraryManagement.Shared.DTOs;

public class BookDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string ISBN { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int TotalCopies { get; set; }
    public int AvailableCopies { get; set; }
    public List<BookCopyDto> Copies { get; set; } = new();
}

public class BookCopyDto
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public BookCopyStatus Status { get; set; }
    public string StatusText => Status.ToString();
    public int? CurrentReservationId { get; set; }
}
