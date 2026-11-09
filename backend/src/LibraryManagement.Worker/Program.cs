using LibraryManagement.Infrastructure.Data;
using LibraryManagement.Infrastructure.Messaging;
using LibraryManagement.Worker;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=library_db;Username=postgres;Password=postgres";

builder.Services.AddDbContext<LibraryDbContext>(options =>
    options.UseNpgsql(connectionString), ServiceLifetime.Transient);

// RabbitMQ Connection Manager & Producer
builder.Services.AddSingleton<RabbitMqConnectionManager>();
builder.Services.AddSingleton<IRabbitMqProducer, RabbitMqProducer>();

// Background Worker Service
builder.Services.AddHostedService<ReservationWorker>();

var host = builder.Build();
host.Run();
