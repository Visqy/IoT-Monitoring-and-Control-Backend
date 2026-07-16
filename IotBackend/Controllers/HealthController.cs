using IotBackend.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace IotBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class HealthController : ControllerBase
{
  private readonly NpgsqlDataSource _dataSource;
  private readonly MqttClientService _mqtt;
  private readonly ILogger<HealthController> _logger;

  public HealthController(NpgsqlDataSource dataSource, MqttClientService mqtt, ILogger<HealthController> logger)
  {
    _dataSource = dataSource;
    _mqtt = mqtt;
    _logger = logger;
  }

  [HttpGet]
  public async Task<IActionResult> Get(CancellationToken cancellationToken)
  {
    var postgresOk = await CheckPostgresAsync(cancellationToken);
    var mqttOk = _mqtt.IsConnected;
    var allOk = postgresOk && mqttOk;

    var payload = new
    {
      status = allOk ? "ok" : "degraded",
      application = "IotBackend",
      timestamp = DateTimeOffset.UtcNow,
      dependencies = new
      {
        postgres = postgresOk ? "up" : "down",
        mqtt = mqttOk ? "up" : "down"
      }
    };

    return allOk
      ? Ok(payload)
      : StatusCode(StatusCodes.Status503ServiceUnavailable, payload);
  }

  private async Task<bool> CheckPostgresAsync(CancellationToken cancellationToken)
  {
    try
    {
      await using var command = _dataSource.CreateCommand("SELECT 1");
      await command.ExecuteScalarAsync(cancellationToken);
      return true;
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Health check Postgres gagal.");
      return false;
    }
  }
}
