namespace LibraryManagement.Shared.Enums;

public enum BookCopyStatus
{
    Available = 0,   // Sẵn sàng cho mượn
    Reserved = 1,    // Đã được giữ trước
    Borrowed = 2,    // Đang được mượn
    Maintenance = 3  // Đang bảo trì / hư hỏng
}
