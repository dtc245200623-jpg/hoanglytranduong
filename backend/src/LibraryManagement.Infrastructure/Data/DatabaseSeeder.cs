using LibraryManagement.Infrastructure.Entities;
using LibraryManagement.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LibraryManagement.Infrastructure.Data;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(LibraryDbContext context, ILogger logger)
    {
        try
        {
            // Tự động tạo bảng nếu chưa có (EnsureCreated hoặc Migrate)
            await context.Database.EnsureCreatedAsync();

            if (await context.Books.AnyAsync())
            {
                logger.LogInformation("Database already contains data. Skipping seeding.");
                return;
            }

            logger.LogInformation("Seeding initial library data...");

            // 1. Tạo Users mẫu
            var users = new List<User>
            {
                new() { FullName = "Nguyễn Văn An", Email = "an.nv@ptit.edu.vn", StudentCode = "B21DCCN001", PhoneNumber = "0912345671" },
                new() { FullName = "Trần Thị Bình", Email = "binh.tt@ptit.edu.vn", StudentCode = "B21DCCN002", PhoneNumber = "0912345672" },
                new() { FullName = "Lê Hoàng Cường", Email = "cuong.lh@ptit.edu.vn", StudentCode = "B21DCCN003", PhoneNumber = "0912345673" },
                new() { FullName = "Phạm Minh Đức", Email = "duc.pm@ptit.edu.vn", StudentCode = "B21DCCN004", PhoneNumber = "0912345674" },
                new() { FullName = "Đỗ Thu Hà", Email = "ha.dt@ptit.edu.vn", StudentCode = "B21DCCN005", PhoneNumber = "0912345675" }
            };
            await context.Users.AddRangeAsync(users);
            await context.SaveChangesAsync();

            // 2. Tạo Books mẫu và các BookCopy tương ứng
            var booksData = new[]
            {
                new
                {
                    Title = "Thiết kế Hệ thống Phân tán (Distributed Systems)",
                    Author = "Andrew S. Tanenbaum",
                    ISBN = "978-0132143011",
                    Category = "Hệ thống phân tán",
                    Description = "Giáo trình nền tảng về kiến trúc phân tán, message passing, đồng thuận và tính nhất quán.",
                    CopiesCount = 3
                },
                new
                {
                    Title = "Microservices Patterns: With examples in Java and .NET",
                    Author = "Chris Richardson",
                    ISBN = "978-1617294549",
                    Category = "Kiến trúc phần mềm",
                    Description = "Các mẫu thiết kế vi dịch vụ, Saga pattern, CQRS, Event-driven architecture.",
                    CopiesCount = 2
                },
                new
                {
                    Title = "Clean Code: A Handbook of Agile Software Craftsmanship",
                    Author = "Robert C. Martin",
                    ISBN = "978-0132350884",
                    Category = "Kỹ nghệ phần mềm",
                    Description = "Quy tắc và phong cách viết mã nguồn sạch, dễ bảo trì, dễ mở rộng.",
                    CopiesCount = 4
                },
                new
                {
                    Title = "Design Patterns: Elements of Reusable Object-Oriented Software",
                    Author = "Erich Gamma, Richard Helm, Ralph Johnson, John Vlissides",
                    ISBN = "978-0201633610",
                    Category = "Mẫu thiết kế",
                    Description = "Cuốn sách kinh điển của Gang of Four (GoF) về 23 mẫu thiết kế hướng đối tượng.",
                    CopiesCount = 2
                },
                new
                {
                    Title = "RabbitMQ in Action: Distributed Messaging for Everyone",
                    Author = "Alvaro Videla, Jason J. W. Williams",
                    ISBN = "978-1935182979",
                    Category = "Message Broker",
                    Description = "Hướng dẫn chi tiết sử dụng RabbitMQ làm Message Broker trong hệ thống phân tán chịu lỗi cao.",
                    CopiesCount = 2
                },
                new
                {
                    Title = "C# 12 and .NET 8 / 10 Modern Cross-Platform Development",
                    Author = "Mark J. Price",
                    ISBN = "978-1837635870",
                    Category = "Lập trình C#",
                    Description = "Sách hướng dẫn chuyên sâu .NET, Minimal API, ASP.NET Core và Entity Framework Core. Bản đặc biệt chỉ có 1 cuốn duy nhất để test tranh chấp!",
                    CopiesCount = 1 // Duy nhất 1 cuốn để demo test tranh chấp!
                }
            };

            foreach (var b in booksData)
            {
                var book = new Book
                {
                    Title = b.Title,
                    Author = b.Author,
                    ISBN = b.ISBN,
                    Category = b.Category,
                    Description = b.Description,
                    TotalCopies = b.CopiesCount,
                    AvailableCopies = b.CopiesCount,
                    CreatedAt = DateTime.UtcNow
                };
                context.Books.Add(book);
                await context.SaveChangesAsync();

                for (int i = 1; i <= b.CopiesCount; i++)
                {
                    var copy = new BookCopy
                    {
                        BookId = book.Id,
                        Barcode = $"BC-{book.Id:D4}-{i:D2}",
                        Status = BookCopyStatus.Available,
                        CreatedAt = DateTime.UtcNow
                    };
                    context.BookCopies.Add(copy);
                }
                await context.SaveChangesAsync();
            }

            logger.LogInformation("Database seeded successfully with {BookCount} books and {UserCount} users.", booksData.Length, users.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding the database.");
            throw;
        }
    }
}
