using System.Text;
using System.Text.Json;
using LibraryManagement.Infrastructure.Data;
using LibraryManagement.Infrastructure.Messaging;
using LibraryManagement.Shared.Contracts;
using LibraryManagement.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace LibraryManagement.Worker;

public class ReservationWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly RabbitMqConnectionManager _connectionManager;
    private readonly ILogger<ReservationWorker> _logger;
    private IModel? _channel;

    public ReservationWorker(
        IServiceProvider serviceProvider,
        RabbitMqConnectionManager connectionManager,
        ILogger<ReservationWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _connectionManager = connectionManager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("=================================================");
        _logger.LogInformation("Reservation Worker Service is starting up...");
        _logger.LogInformation("Waiting for RabbitMQ broker and Database...");
        _logger.LogInformation("=================================================");

        // Chờ kết nối tới RabbitMQ và thiết lập consumer với cơ chế phục hồi
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                SetupRabbitMqConsumer(stoppingToken);
                _logger.LogInformation("--> [Worker] Listening for messages on queue '{Queue}'...", RabbitMqConstants.QueueName);
                
                // Giữ vòng lặp chạy đến khi bị hủy
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "--> [Worker] Connection lost or failed. Re-attempting in 5 seconds...");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private void SetupRabbitMqConsumer(CancellationToken stoppingToken)
    {
        _channel?.Dispose();
        _channel = _connectionManager.CreateModel();

        // Giới hạn prefetch = 1 để phân bổ tải công bằng giữa các worker instance
        _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += async (model, ea) =>
        {
            var deliveryTag = ea.DeliveryTag;
            var correlationId = ea.BasicProperties.CorrelationId ?? Guid.NewGuid().ToString();

            try
            {
                var body = ea.Body.ToArray();
                var json = Encoding.UTF8.GetString(body);
                var message = JsonSerializer.Deserialize<ReservationRequestedMessage>(json);

                if (message == null)
                {
                    _logger.LogWarning("--> [Worker] Received null or invalid message format. Acking and dropping.");
                    _channel.BasicAck(deliveryTag, multiple: false);
                    return;
                }

                _logger.LogInformation(
                    "--> [Worker] Received reservation request: ID={ReservationId}, BookId={BookId}, UserId={UserId}, CorrelationId={CorrelationId}",
                    message.ReservationId,
                    message.BookId,
                    message.UserId,
                    correlationId
                );

                // Xử lý kiểm tra số bản khả dụng và xác nhận hoặc từ chối
                var success = await ProcessReservationAsync(message, correlationId);

                if (success)
                {
                    _channel.BasicAck(deliveryTag, multiple: false);
                }
                else
                {
                    // Thất bại tạm thời -> xử lý retry hoặc đẩy sang Dead-Letter Queue
                    await HandleFailureAsync(deliveryTag, message, correlationId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "--> [Worker] Unexpected error processing message with CorrelationId: {CorrelationId}", correlationId);
                // Từ chối không requeue để chuyển sang Dead-Letter Queue (DLQ)
                _channel.BasicNack(deliveryTag, multiple: false, requeue: false);
            }
        };

        _channel.BasicConsume(
            queue: RabbitMqConstants.QueueName,
            autoAck: false,
            consumer: consumer
        );
    }

    private async Task<bool> ProcessReservationAsync(ReservationRequestedMessage message, string correlationId)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();

        // 1. Idempotency Check: Kiểm tra đơn đặt có tồn tại và đang ở trạng thái Pending không
        var reservation = await db.Reservations
            .Include(r => r.Book)
            .FirstOrDefaultAsync(r => r.Id == message.ReservationId);

        if (reservation == null)
        {
            _logger.LogWarning("--> [Worker] Reservation with ID={ReservationId} not found in DB. Skipping.", message.ReservationId);
            return true; // Ack để không lặp lại
        }

        if (reservation.Status != ReservationStatus.Pending)
        {
            _logger.LogInformation(
                "--> [Worker] Idempotency: Reservation ID={ReservationId} already processed (Current status: {Status}). Skipping.",
                reservation.Id,
                reservation.Status
            );
            return true;
        }

        // 2. Bắt đầu Transaction kiểm tra số bản khả dụng và giữ sách
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var book = await db.Books
                .Include(b => b.Copies)
                .FirstOrDefaultAsync(b => b.Id == message.BookId);

            if (book == null)
            {
                reservation.Status = ReservationStatus.Rejected;
                reservation.Notes = "Từ chối: Cuốn sách này không còn tồn tại trong cơ sở dữ liệu.";
                reservation.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
                return true;
            }

            // Tìm bản sao sách đang ở trạng thái Available
            var availableCopy = book.Copies
                .FirstOrDefault(c => c.Status == BookCopyStatus.Available);

            if (availableCopy != null && book.AvailableCopies > 0)
            {
                // CÒN BẢN KHẢ DỤNG: Xác nhận giữ sách
                availableCopy.Status = BookCopyStatus.Reserved;
                availableCopy.CurrentReservationId = reservation.Id;
                book.AvailableCopies = Math.Max(0, book.AvailableCopies - 1);

                reservation.Status = ReservationStatus.Reserved;
                reservation.BookCopyId = availableCopy.Id;
                reservation.Notes = $"Đã giữ thành công bản sao [{availableCopy.Barcode}]. Quý bạn đọc vui lòng đến thư viện nhận sách trong vòng 48 giờ.";
                reservation.UpdatedAt = DateTime.UtcNow;

                await db.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation(
                    "==> [Worker - SUCCESS] Reservation ID={ReservationId} ACCEPTED (Reserved). Book: '{Title}', Barcode: '{Barcode}', Remaining: {Remaining}",
                    reservation.Id,
                    book.Title,
                    availableCopy.Barcode,
                    book.AvailableCopies
                );
            }
            else
            {
                // HẾT BẢN KHẢ DỤNG: Từ chối yêu cầu
                reservation.Status = ReservationStatus.Rejected;
                reservation.Notes = "Từ chối: Hiện tại tất cả các bản sao của sách này đều đã được mượn hoặc giữ trước.";
                reservation.UpdatedAt = DateTime.UtcNow;

                await db.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation(
                    "==> [Worker - REJECTED] Reservation ID={ReservationId} REJECTED. Book: '{Title}' has 0 available copies.",
                    reservation.Id,
                    book.Title
                );
            }

            return true;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "--> [Worker] Database error while processing reservation ID={ReservationId}.", message.ReservationId);
            return false;
        }
    }

    private async Task HandleFailureAsync(ulong deliveryTag, ReservationRequestedMessage message, string correlationId)
    {
        if (_channel == null) return;

        message.RetryCount++;

        if (message.RetryCount <= RabbitMqConstants.MaxRetries)
        {
            _logger.LogWarning(
                "--> [Worker] Retry {RetryCount}/{MaxRetries} for Reservation ID={ReservationId}...",
                message.RetryCount,
                RabbitMqConstants.MaxRetries,
                message.ReservationId
            );

            // Ack tin nhắn cũ và republish với RetryCount tăng dần sau 2 giây delay
            await Task.Delay(2000);
            var producer = _serviceProvider.GetRequiredService<IRabbitMqProducer>();
            await producer.PublishAsync(RabbitMqConstants.RoutingKey, message, correlationId);
            _channel.BasicAck(deliveryTag, multiple: false);
        }
        else
        {
            _logger.LogError(
                "--> [Worker] Max retries reached for Reservation ID={ReservationId}. Moving to Dead-Letter Queue (DLQ).",
                message.ReservationId
            );

            // Nack không requeue -> RabbitMQ tự động forward sang Dead-Letter Queue theo cấu hình
            _channel.BasicNack(deliveryTag, multiple: false, requeue: false);

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
                var reservation = await db.Reservations.FindAsync(message.ReservationId);
                if (reservation != null && reservation.Status == ReservationStatus.Pending)
                {
                    reservation.Notes = "Lỗi xử lý hệ thống: Đã chuyển sang hàng đợi kiểm toán (DLQ).";
                    reservation.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update reservation failure notes in DB.");
            }
        }
    }

    public override void Dispose()
    {
        try
        {
            _channel?.Dispose();
        }
        catch { }
        base.Dispose();
    }
}
