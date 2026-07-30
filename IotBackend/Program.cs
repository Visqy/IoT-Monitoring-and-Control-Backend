using System.Text;
using IotBackend.BackgroundServices;
using IotBackend.Infrastructure;
using IotBackend.Options;
using IotBackend.Repositories;
using IotBackend.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Scalar.AspNetCore;

EnvFile.Load(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

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

builder.Services.AddSingleton<RealtimeBroadcaster>();

builder.Services.AddScoped<TelemetryRepository>();
builder.Services.AddScoped<DeviceStateRepository>();
builder.Services.AddScoped<DeviceRepository>();
builder.Services.AddScoped<TelemetryService>();

builder.Services.AddScoped<DeviceService>();

builder.Services.AddScoped<RelayCommandRepository>();
builder.Services.AddScoped<RelayCommandService>();

builder.Services.Configure<RelayOptions>(builder.Configuration.GetSection(RelayOptions.SectionName));
builder.Services.AddHostedService<RelayCommandTimeoutService>();

builder.Services.AddScoped<RfidCardRepository>();
builder.Services.AddScoped<RfidEventRepository>();
builder.Services.AddScoped<RfidService>();

builder.Services.Configure<DeviceOfflineOptions>(builder.Configuration.GetSection(DeviceOfflineOptions.SectionName));
builder.Services.AddHostedService<DeviceOfflineSweepService>();

builder.Services.AddHostedService<MqttSubscriberService>();

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.AddScoped<AuthService>();

var authSigningKey = builder.Configuration["Auth:JwtSigningKey"];
if (string.IsNullOrWhiteSpace(authSigningKey))
{
    throw new InvalidOperationException(
        "Auth:JwtSigningKey belum diset. Set lewat user-secrets atau .env, jangan taruh di appsettings.json.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authSigningKey))
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.InitializeAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
