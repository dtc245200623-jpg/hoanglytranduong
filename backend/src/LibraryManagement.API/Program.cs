using LibraryManagement.Infrastructure.Data;
using LibraryManagement.Infrastructure.Entities;
using LibraryManagement.Infrastructure.Messaging;
using LibraryManagement.Shared.Contracts;
using LibraryManagement.Shared.DTOs;
using LibraryManagement.Shared.Enums;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// 2. Database Context (PostgreSQL)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=library_db;Username=postgres;Password=postgres";

builder.Services.AddDbContext<LibraryDbContext>(options =>
    options.UseNpgsql(connectionString));

// 3. RabbitMQ Services
builder.Services.AddSingleton<RabbitMqConnectionManager>();
builder.Services.AddScoped<IRabbitMqProducer, RabbitMqProducer>();

// 4. CORS: Cho phép Frontend độc lập gọi API từ bất kỳ nguồn nào
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// 5. OpenAPI
builder.Services.AddOpenApi();

var app = builder.Build();

// 6. Tự động áp dụng Migration và Seed Database
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = services.GetRequiredService<LibraryDbContext>();
        logger.LogInformation("Checking database connection and applying migrations/schema...");
        await DatabaseSeeder.SeedAsync(db, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while initializing database.");
    }
}

// 7. Middleware Pipeline
app.UseCors("AllowAll");

app.MapOpenApi();

// ==========================================
// API ENDPOINTS
// ==========================================

// Health Check
app.MapGet("/health", async (LibraryDbContext db, RabbitMqConnectionManager rabbitManager) =>
{
    var dbOk = false;
    try
    {
        dbOk = await db.Database.CanConnectAsync();
    }
    catch { }

    var rabbitOk = false;
    try
    {
        rabbitOk = rabbitManager.IsConnected;
    }
    catch { }

    return Results.Ok(new
    {
        Status = dbOk ? "Healthy" : "Degraded",
        Timestamp = DateTime.UtcNow,
        Components = new
        {
            Database = dbOk ? "Connected" : "Disconnected",
            RabbitMQ = rabbitOk ? "Connected" : "Disconnected"
        }
    });
})
.WithName("HealthCheck")
.WithTags("System");

// 1. GET /books - Tra cứu danh sách và chi tiết sách, số bản khả dụng
app.MapGet("/books", async (LibraryDbContext db, string? search, string? category) =>
{
    var query = db.Books.Include(b => b.Copies).AsQueryable();

    if (!string.IsNullOrWhiteSpace(search))
    {
        var term = search.Trim().ToLower();
        query = query.Where(b => b.Title.ToLower().Contains(term) ||
                                 b.Author.ToLower().Contains(term) ||
                                 b.ISBN.ToLower().Contains(term));
    }

    if (!string.IsNullOrWhiteSpace(category))
    {
        query = query.Where(b => b.Category.ToLower() == category.Trim().ToLower());
    }

    var books = await query
        .OrderBy(b => b.Id)
        .Select(b => new BookDto
        {
            Id = b.Id,
            Title = b.Title,
            Author = b.Author,
            ISBN = b.ISBN,
            Category = b.Category,
            Description = b.Description,
            TotalCopies = b.TotalCopies,
            AvailableCopies = b.AvailableCopies,
            Copies = b.Copies.Select(c => new BookCopyDto
            {
                Id = c.Id,
                BookId = c.BookId,
                Barcode = c.Barcode,
                Status = c.Status,
                CurrentReservationId = c.CurrentReservationId
            }).ToList()
        })
        .ToListAsync();

    return Results.Ok(books);
})
.WithName("GetBooks")
.WithTags("Books");

// 2. GET /books/{id} - Chi tiết sách và các bản sao
app.MapGet("/books/{id:int}", async (int id, LibraryDbContext db) =>
{
    var book = await db.Books
        .Include(b => b.Copies)
        .FirstOrDefaultAsync(b => b.Id == id);

    if (book == null)
    {
        return Results.NotFound(new { message = $"Không tìm thấy sách với ID = {id}" });
    }

    var dto = new BookDto
    {
        Id = book.Id,
        Title = book.Title,
        Author = book.Author,
        ISBN = book.ISBN,
        Category = book.Category,
        Description = book.Description,
        TotalCopies = book.TotalCopies,
        AvailableCopies = book.AvailableCopies,
        Copies = book.Copies.Select(c => new BookCopyDto
        {
            Id = c.Id,
            BookId = c.BookId,
            Barcode = c.Barcode,
            Status = c.Status,
            CurrentReservationId = c.CurrentReservationId
        }).ToList()
    };

    return Results.Ok(dto);
})
.WithName("GetBookById")
.WithTags("Books");

// ==========================================
// AUTHENTICATION & USERS
// ==========================================

// POST /auth/login - Đăng nhập phân quyền (Thủ thư / Sinh viên)
app.MapPost("/auth/login", async (LoginRequest req, LibraryDbContext db, ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
    {
        return Results.BadRequest(new LoginResponse { Success = false, Message = "Vui lòng nhập tên đăng nhập và mật khẩu." });
    }

    var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == req.Username.Trim().ToLower());
    if (user == null || user.Password != req.Password.Trim())
    {
        logger.LogWarning("--> [Auth] Failed login attempt for username: {Username}", req.Username);
        return Results.BadRequest(new LoginResponse { Success = false, Message = "Tên đăng nhập hoặc mật khẩu không chính xác!" });
    }

    var userDto = new UserDto
    {
        Id = user.Id,
        Username = user.Username,
        FullName = user.FullName,
        Email = user.Email,
        StudentCode = user.StudentCode,
        PhoneNumber = user.PhoneNumber,
        Role = user.Role
    };

    var token = $"auth_token_{user.Id}_{user.Role}_{Guid.NewGuid():N}";
    logger.LogInformation("--> [Auth] User {Username} logged in successfully with Role: {Role}", user.Username, user.Role);

    return Results.Ok(new LoginResponse
    {
        Success = true,
        Message = $"Đăng nhập thành công! Chào mừng {user.FullName}",
        Token = token,
        User = userDto
    });
})
.WithName("Login")
.WithTags("Auth");

// GET /users - Lấy danh sách bạn đọc (hỗ trợ chọn người mượn ở UI)
app.MapGet("/users", async (LibraryDbContext db) =>
{
    var users = await db.Users
        .OrderBy(u => u.Id)
        .Select(u => new UserDto
        {
            Id = u.Id,
            Username = u.Username,
            FullName = u.FullName,
            Email = u.Email,
            StudentCode = u.StudentCode,
            PhoneNumber = u.PhoneNumber,
            Role = u.Role
        })
        .ToListAsync();

    return Results.Ok(users);
})
.WithName("GetUsers")
.WithTags("Users");

// 3. POST /reservations - Gửi yêu cầu đặt mượn sách -> đưa vào RabbitMQ xử lý bất đồng bộ
app.MapPost("/reservations", async (
    CreateReservationRequest request,
    LibraryDbContext db,
    IRabbitMqProducer rabbitProducer,
    ILogger<Program> logger) =>
{
    // Kiểm tra user tồn tại
    var user = await db.Users.FindAsync(request.UserId);
    if (user == null)
    {
        return Results.BadRequest(new { message = $"Người dùng với ID = {request.UserId} không tồn tại." });
    }

    // Kiểm tra sách tồn tại
    var book = await db.Books.FindAsync(request.BookId);
    if (book == null)
    {
        return Results.BadRequest(new { message = $"Sách với ID = {request.BookId} không tồn tại." });
    }

    var correlationId = Guid.NewGuid().ToString();

    // Tạo bản ghi Reservation trạng thái ban đầu là Pending
    var reservation = new Reservation
    {
        UserId = request.UserId,
        BookId = request.BookId,
        Status = ReservationStatus.Pending,
        CorrelationId = correlationId,
        CreatedAt = DateTime.UtcNow,
        Notes = "Đã tiếp nhận yêu cầu, đang xử lý kiểm tra số bản khả dụng qua hàng đợi RabbitMQ..."
    };

    db.Reservations.Add(reservation);
    await db.SaveChangesAsync();

    logger.LogInformation(
        "--> [API] Created Reservation ID {ReservationId} (Pending) for User {UserId}, Book {BookId}. Publishing to RabbitMQ...",
        reservation.Id,
        reservation.UserId,
        reservation.BookId
    );

    // Đưa yêu cầu vào RabbitMQ
    var message = new ReservationRequestedMessage
    {
        ReservationId = reservation.Id,
        BookId = reservation.BookId,
        UserId = reservation.UserId,
        CorrelationId = correlationId,
        RequestedAt = reservation.CreatedAt
    };

    try
    {
        await rabbitProducer.PublishAsync(
            routingKey: RabbitMqConstants.RoutingKey,
            message: message,
            correlationId: correlationId
        );
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to publish reservation request to RabbitMQ.");
        reservation.Notes = "Lỗi kết nối Message Broker. Yêu cầu sẽ được xử lý khi broker phục hồi.";
        await db.SaveChangesAsync();
    }

    var dto = new ReservationDto
    {
        Id = reservation.Id,
        UserId = reservation.UserId,
        UserName = user.FullName,
        StudentCode = user.StudentCode,
        BookId = reservation.BookId,
        BookTitle = book.Title,
        BookAuthor = book.Author,
        Status = reservation.Status,
        CorrelationId = reservation.CorrelationId,
        Notes = reservation.Notes,
        CreatedAt = reservation.CreatedAt
    };

    return Results.Created($"/reservations/{reservation.Id}", dto);
})
.WithName("CreateReservation")
.WithTags("Reservations");

// 4. GET /reservations/{id} - Tra cứu trạng thái yêu cầu đặt mượn
app.MapGet("/reservations/{id:int}", async (int id, LibraryDbContext db) =>
{
    var res = await db.Reservations
        .Include(r => r.User)
        .Include(r => r.Book)
        .Include(r => r.BookCopy)
        .FirstOrDefaultAsync(r => r.Id == id);

    if (res == null)
    {
        return Results.NotFound(new { message = $"Không tìm thấy đơn đặt mượn ID = {id}" });
    }

    var dto = new ReservationDto
    {
        Id = res.Id,
        UserId = res.UserId,
        UserName = res.User.FullName,
        StudentCode = res.User.StudentCode,
        BookId = res.BookId,
        BookTitle = res.Book.Title,
        BookAuthor = res.Book.Author,
        BookCopyId = res.BookCopyId,
        CopyBarcode = res.BookCopy?.Barcode,
        Status = res.Status,
        CorrelationId = res.CorrelationId,
        Notes = res.Notes,
        CreatedAt = res.CreatedAt,
        UpdatedAt = res.UpdatedAt
    };

    return Results.Ok(dto);
})
.WithName("GetReservationById")
.WithTags("Reservations");

// 5. GET /users/{userId}/reservations - Tra cứu danh sách sách đang mượn hoặc đã đặt của một người dùng
app.MapGet("/users/{userId:int}/reservations", async (int userId, LibraryDbContext db) =>
{
    var userExists = await db.Users.AnyAsync(u => u.Id == userId);
    if (!userExists)
    {
        return Results.NotFound(new { message = $"Không tìm thấy người dùng ID = {userId}" });
    }

    var list = await db.Reservations
        .Include(r => r.User)
        .Include(r => r.Book)
        .Include(r => r.BookCopy)
        .Where(r => r.UserId == userId)
        .OrderByDescending(r => r.CreatedAt)
        .Select(r => new ReservationDto
        {
            Id = r.Id,
            UserId = r.UserId,
            UserName = r.User.FullName,
            StudentCode = r.User.StudentCode,
            BookId = r.BookId,
            BookTitle = r.Book.Title,
            BookAuthor = r.Book.Author,
            BookCopyId = r.BookCopyId,
            CopyBarcode = r.BookCopy != null ? r.BookCopy.Barcode : null,
            Status = r.Status,
            CorrelationId = r.CorrelationId,
            Notes = r.Notes,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt
        })
        .ToListAsync();

    return Results.Ok(list);
})
.WithName("GetUserReservations")
.WithTags("Reservations");

// GET /reservations - Lấy tất cả đơn đặt (phục vụ danh sách quản trị chung)
app.MapGet("/reservations", async (LibraryDbContext db, ReservationStatus? status) =>
{
    var query = db.Reservations
        .Include(r => r.User)
        .Include(r => r.Book)
        .Include(r => r.BookCopy)
        .AsQueryable();

    if (status.HasValue)
    {
        query = query.Where(r => r.Status == status.Value);
    }

    var list = await query
        .OrderByDescending(r => r.CreatedAt)
        .Select(r => new ReservationDto
        {
            Id = r.Id,
            UserId = r.UserId,
            UserName = r.User.FullName,
            StudentCode = r.User.StudentCode,
            BookId = r.BookId,
            BookTitle = r.Book.Title,
            BookAuthor = r.Book.Author,
            BookCopyId = r.BookCopyId,
            CopyBarcode = r.BookCopy != null ? r.BookCopy.Barcode : null,
            Status = r.Status,
            CorrelationId = r.CorrelationId,
            Notes = r.Notes,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt
        })
        .ToListAsync();

    return Results.Ok(list);
})
.WithName("GetAllReservations")
.WithTags("Reservations");

// POST /reservations/{id}/borrow - Thủ thư xác nhận độc giả đã đến nhận sách (Reserved -> Borrowed)
app.MapPost("/reservations/{id:int}/borrow", async (int id, LibraryDbContext db, ILogger<Program> logger) =>
{
    var res = await db.Reservations
        .Include(r => r.BookCopy)
        .FirstOrDefaultAsync(r => r.Id == id);

    if (res == null)
    {
        return Results.NotFound(new { message = $"Không tìm thấy đơn đặt ID = {id}" });
    }

    if (res.Status != ReservationStatus.Reserved)
    {
        return Results.BadRequest(new { message = $"Không thể nhận sách vì đơn này đang ở trạng thái {res.Status} (cần là Reserved)." });
    }

    res.Status = ReservationStatus.Borrowed;
    res.UpdatedAt = DateTime.UtcNow;
    res.Notes = "Độc giả đã nhận sách tại quầy thư viện.";

    if (res.BookCopy != null)
    {
        res.BookCopy.Status = BookCopyStatus.Borrowed;
    }

    await db.SaveChangesAsync();
    logger.LogInformation("--> Reservation {Id} changed to Borrowed.", id);

    return Results.Ok(new { message = "Đã cập nhật trạng thái: Đã nhận sách (Borrowed)", reservationId = id });
})
.WithName("ConfirmBorrow")
.WithTags("Reservations");

// POST /reservations/{id}/return - Thủ thư xác nhận độc giả đã trả sách (Borrowed -> Returned)
app.MapPost("/reservations/{id:int}/return", async (int id, LibraryDbContext db, ILogger<Program> logger) =>
{
    var res = await db.Reservations
        .Include(r => r.Book)
        .Include(r => r.BookCopy)
        .FirstOrDefaultAsync(r => r.Id == id);

    if (res == null)
    {
        return Results.NotFound(new { message = $"Không tìm thấy đơn đặt ID = {id}" });
    }

    if (res.Status != ReservationStatus.Borrowed && res.Status != ReservationStatus.Reserved)
    {
        return Results.BadRequest(new { message = $"Không thể trả sách cho đơn có trạng thái {res.Status}." });
    }

    res.Status = ReservationStatus.Returned;
    res.UpdatedAt = DateTime.UtcNow;
    res.Notes = "Độc giả đã trả sách. Bản sao sách đã được hoàn lại kho.";

    if (res.BookCopy != null)
    {
        res.BookCopy.Status = BookCopyStatus.Available;
        res.BookCopy.CurrentReservationId = null;
    }

    // Hoàn lại 1 bản khả dụng cho sách
    if (res.Book != null)
    {
        res.Book.AvailableCopies = Math.Min(res.Book.TotalCopies, res.Book.AvailableCopies + 1);
    }

    await db.SaveChangesAsync();
    logger.LogInformation("--> Reservation {Id} changed to Returned. Restored 1 available copy for Book {BookId}.", id, res.BookId);

    return Results.Ok(new { message = "Đã trả sách thành công! Bản sao đã khả dụng lại trong kho.", reservationId = id });
})
.WithName("ConfirmReturn")
.WithTags("Reservations");

app.Run();
