using IotBackend.BackgroundServices;
using IotBackend.Infrastructure;
using IotBackend.Options;
using IotBackend.Repositories;
using IotBackend.Services;
using Npgsql;
using Scalar.AspNetCore;

EnvFile.Load(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

// Kolom snake_case (device_id) -> property PascalCase (DeviceId) otomatis, dipakai semua Repository.
Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var connectionString = builder.Configuration.GetConnectionString("Postgres");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:Postgres belum diset. Copy IotBackend/.env.example jadi .env, " +
        "lalu isi ConnectionStrings__Postgres dengan nilai asli.");
}
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<DatabaseInitializer>();

builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection(MqttOptions.SectionName));
builder.Services.AddSingleton<MqttClientService>();

builder.Services.AddScoped<TelemetryRepository>();
builder.Services.AddScoped<DeviceStateRepository>();
builder.Services.AddScoped<DeviceRepository>();
builder.Services.AddScoped<TelemetryService>();

builder.Services.AddScoped<DeviceService>();

builder.Services.AddScoped<RelayCommandRepository>();
builder.Services.AddScoped<RelayCommandService>();

builder.Services.Configure<RelayOptions>(builder.Configuration.GetSection(RelayOptions.SectionName));
builder.Services.AddHostedService<RelayCommandTimeoutService>();

builder.Services.Configure<DeviceOfflineOptions>(builder.Configuration.GetSection(DeviceOfflineOptions.SectionName));
builder.Services.AddHostedService<DeviceOfflineSweepService>();

builder.Services.AddHostedService<MqttSubscriberService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.InitializeAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();                 // dokumen OpenAPI di /openapi/v1.json
    app.MapScalarApiReference();      // UI interaktif di /scalar/v1
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
