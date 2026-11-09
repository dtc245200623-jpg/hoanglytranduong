using LibraryManagement.Infrastructure.Entities;
using LibraryManagement.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LibraryManagement.Infrastructure.Data;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(LibraryDbContext context, ILogger logger)
    {
        const int maxRetries = 10;
        for (var retry = 1; retry <= maxRetries; retry++)
        {
            try
            {
                // Tự động tạo bảng nếu chưa có (EnsureCreated hoặc Migrate)
                await context.Database.EnsureCreatedAsync();
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Database is not ready yet (attempt {Attempt}/{MaxRetries}). Retrying in 3s...", retry, maxRetries);
                if (retry == maxRetries) throw;
                await Task.Delay(3000);
            }
        }

        try
        {
            // Tự động bổ sung cột nếu bảng đã tồn tại từ trước
            try
            {
                await context.Database.ExecuteSqlRawAsync(@"
                    ALTER TABLE ""Users"" ADD COLUMN IF NOT EXISTS ""Username"" VARCHAR(50) DEFAULT 'user';
                    ALTER TABLE ""Users"" ADD COLUMN IF NOT EXISTS ""Password"" VARCHAR(100) DEFAULT '123';
                    ALTER TABLE ""Users"" ADD COLUMN IF NOT EXISTS ""Role"" INTEGER DEFAULT 0;
                ");
            }
            catch { }

            // Đảm bảo luôn có tài khoản Thủ thư và Sinh viên
            var librarian = await context.Users.FirstOrDefaultAsync(u => u.Username == "thuthu");
            if (librarian == null)
            {
                context.Users.Add(new User
                {
                    Username = "thuthu",
                    Password = "123",
                    FullName = "Cô Trần Mai Lan (Thủ thư trưởng)",
                    Email = "thuthu@ptit.edu.vn",
                    StudentCode = "NV001",
                    PhoneNumber = "0988776655",
                    Role = UserRole.Librarian
                });
                await context.SaveChangesAsync();
            }

            if (await context.Books.AnyAsync())
            {
                // Cập nhật lại thông tin đăng nhập cho các user cũ nếu thiếu
                var existingUsers = await context.Users.ToListAsync();
                var studentUsers = new Dictionary<string, (string username, string studentCode)>
                {
                    { "Nguyễn Văn An", ("sv_an", "B21DCCN001") },
                    { "Trần Thị Bình", ("sv_binh", "B21DCCN002") },
                    { "Lê Hoàng Cường", ("sv_cuong", "B21DCCN003") },
                    { "Phạm Minh Đức", ("sv_duc", "B21DCCN004") },
                    { "Đỗ Thu Hà", ("sv_ha", "B21DCCN005") }
                };

                foreach (var user in existingUsers)
                {
                    if (studentUsers.TryGetValue(user.FullName, out var info))
                    {
                        user.Username = info.username;
                        user.Password = "123";
                        user.Role = UserRole.Student;
                    }
                }
                await context.SaveChangesAsync();

                logger.LogInformation("Database already contains data. User accounts updated.");
                return;
            }

            logger.LogInformation("Seeding initial library data...");

            // 1. Tạo Users mẫu kèm tài khoản phân quyền
            var users = new List<User>
            {
                new() { Username = "thuthu", Password = "123", FullName = "Cô Trần Mai Lan (Thủ thư trưởng)", Email = "thuthu@ptit.edu.vn", StudentCode = "NV001", PhoneNumber = "0988776655", Role = UserRole.Librarian },
                new() { Username = "sv_an", Password = "123", FullName = "Nguyễn Văn An", Email = "an.nv@ptit.edu.vn", StudentCode = "B21DCCN001", PhoneNumber = "0912345671", Role = UserRole.Student },
                new() { Username = "sv_binh", Password = "123", FullName = "Trần Thị Bình", Email = "binh.tt@ptit.edu.vn", StudentCode = "B21DCCN002", PhoneNumber = "0912345672", Role = UserRole.Student },
                new() { Username = "sv_cuong", Password = "123", FullName = "Lê Hoàng Cường", Email = "cuong.lh@ptit.edu.vn", StudentCode = "B21DCCN003", PhoneNumber = "0912345673", Role = UserRole.Student },
                new() { Username = "sv_duc", Password = "123", FullName = "Phạm Minh Đức", Email = "duc.pm@ptit.edu.vn", StudentCode = "B21DCCN004", PhoneNumber = "0912345674", Role = UserRole.Student },
                new() { Username = "sv_ha", Password = "123", FullName = "Đỗ Thu Hà", Email = "ha.dt@ptit.edu.vn", StudentCode = "B21DCCN005", PhoneNumber = "0912345675", Role = UserRole.Student }
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
