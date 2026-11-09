using LibraryManagement.Shared.Enums;

namespace LibraryManagement.Shared.DTOs;

public class UpdateReservationStatusRequest
{
    public ReservationStatus Status { get; set; }
    public string? Notes { get; set; }
}
