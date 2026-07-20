using IotBackend.BackgroundServices;
using IotBackend.Infrastructure;
using IotBackend.Options;
using IotBackend.Repositories;
using IotBackend.Services;
using Npgsql;
using Scalar.AspNetCore;

// Muat .env (kalau ada) sebagai environment variable proses SEBELUM CreateBuilder,
// supaya provider environment variable bawaan ASP.NET Core bisa membacanya.
EnvFile.Load(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
// Format error response standar ProblemDetails untuk NotFound()/Problem()/dll (docs/API_CONTRACT.md).
builder.Services.AddProblemDetails();

// --- PostgreSQL ---
// NpgsqlDataSource sebagai SINGLETON (mengelola connection pool sendiri, lihat CLAUDE.md §4).
var connectionString = builder.Configuration.GetConnectionString("Postgres");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:Postgres belum diset. Copy IotBackend/.env.example jadi .env, " +
        "lalu isi ConnectionStrings__Postgres dengan nilai asli.");
}
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<DatabaseInitializer>();

// --- MQTT (HiveMQ Cloud, TLS) ---
builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection(MqttOptions.SectionName));
builder.Services.AddSingleton<MqttClientService>();               // satu koneksi terkelola

// --- Telemetry pipeline (Fase 2): scoped, dipakai per-pesan lewat scope di subscriber ---
builder.Services.AddScoped<TelemetryRepository>();
builder.Services.AddScoped<DeviceStateRepository>();
builder.Services.AddScoped<DeviceRepository>();
builder.Services.AddScoped<TelemetryService>();

// --- Read layer (Fase 3) ---
builder.Services.AddScoped<DeviceService>();

// --- Relay command (Fase 4) ---
builder.Services.AddScoped<RelayCommandRepository>();
builder.Services.AddScoped<RelayCommandService>();

// --- Hardening (Fase 5) ---
builder.Services.Configure<RelayOptions>(builder.Configuration.GetSection(RelayOptions.SectionName));
builder.Services.AddHostedService<RelayCommandTimeoutService>(); // auto-timeout command 'sent' yang basi

// MQTT subscriber: connect + subscribe +/pzem & +/relay/state (auto-reconnect), proses ke DB
builder.Services.AddHostedService<MqttSubscriberService>();

var app = builder.Build();

// Inisialisasi schema DB saat startup (non-fatal kalau DB belum siap).
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.InitializeAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();                 // dokumen OpenAPI di /openapi/v1.json
    app.MapScalarApiReference();      // UI interaktif di /scalar/v1
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
