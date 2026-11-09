namespace LibraryManagement.Shared.Enums;

public enum ReservationStatus
{
    Pending = 0,    // Đang chờ worker xử lý trong hàng đợi RabbitMQ
    Reserved = 1,   // Đã giữ sách thành công
    Rejected = 2,   // Bị từ chối do hết sách khả dụng
    Borrowed = 3,   // Độc giả đã đến nhận sách
    Returned = 4    // Độc giả đã trả sách, hoàn lại kho
}
