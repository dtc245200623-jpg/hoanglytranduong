namespace LibraryManagement.Shared.Contracts;

public class ReservationRequestedMessage
{
    public int ReservationId { get; set; }
    public int BookId { get; set; }
    public int UserId { get; set; }
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public int RetryCount { get; set; } = 0;
}
