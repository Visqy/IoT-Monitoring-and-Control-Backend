using System.Globalization;
using System.Text.Json;
using IotBackend.Contracts;
using IotBackend.Models;
using IotBackend.Repositories;

namespace IotBackend.Services;

/// <summary>
/// Business rule telemetry: deserialize + validasi payload, normalisasi timestamp, tentukan
/// status, lalu simpan ke history (<c>telemetry</c>) dan snapshot (<c>device_current_state</c>).
/// Dipanggil oleh MqttSubscriberService lewat scope baru per pesan (CLAUDE.md §4).
/// </summary>
public sealed class TelemetryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // Format timestamp firmware saat ini (belum ISO 8601, belum ada timezone).
    private const string DeviceTimestampFormat = "yyyy-MM-dd HH:mm:ss";

    private readonly TelemetryRepository _telemetryRepository;
    private readonly DeviceStateRepository _deviceStateRepository;
    private readonly DeviceRepository _deviceRepository;
    private readonly ILogger<TelemetryService> _logger;

    public TelemetryService(
        TelemetryRepository telemetryRepository,
        DeviceStateRepository deviceStateRepository,
        DeviceRepository deviceRepository,
        ILogger<TelemetryService> logger)
    {
        _telemetryRepository = telemetryRepository;
        _deviceStateRepository = deviceStateRepository;
        _deviceRepository = deviceRepository;
        _logger = logger;
    }

    public async Task ProcessTelemetryAsync(
        string deviceId,
        string topic,
        string rawPayload,
        CancellationToken cancellationToken = default)
    {
        TelemetryPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<TelemetryPayload>(rawPayload, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Payload telemetry dari {DeviceId} bukan JSON valid, diabaikan. Raw: {Raw}", deviceId, rawPayload);
            return;
        }

        if (payload is null)
        {
            _logger.LogWarning("Payload telemetry dari {DeviceId} kosong/null setelah deserialize, diabaikan.", deviceId);
            return;
        }

        var deviceTimestamp = ParseDeviceTimestamp(payload.Timestamp);
        var receivedAt = DateTimeOffset.UtcNow;

        var record = new TelemetryRecord
        {
            DeviceId = deviceId,
            Topic = topic,
            VoltageA = payload.VoltageA,
            VoltageB = payload.VoltageB,
            FrequencyA = payload.FreqA,
            FrequencyB = payload.FreqB,
            DeviceTimestamp = deviceTimestamp,
            RawPayload = rawPayload
        };
        await _telemetryRepository.InsertAsync(record, cancellationToken);

        var state = new DeviceCurrentState
        {
            DeviceId = deviceId,
            Status = "online", // device sedang mengirim data; deteksi abnormal (threshold) menyusul
            VoltageA = payload.VoltageA,
            VoltageB = payload.VoltageB,
            FrequencyA = payload.FreqA,
            FrequencyB = payload.FreqB,
            LastSeen = receivedAt
        };
        await _deviceStateRepository.UpsertFromTelemetryAsync(state, cancellationToken);

        // Auto-register ke tabel devices (master data) supaya GET /api/devices tidak kosong
        // begitu ada device baru mengirim telemetry. name/location tetap NULL (data manual).
        await _deviceRepository.UpsertAsync(deviceId, state.Status, cancellationToken);

        _logger.LogInformation(
            "Telemetry {DeviceId} tersimpan (Va={Va} Vb={Vb} Fa={Fa} Fb={Fb}).",
            deviceId, payload.VoltageA, payload.VoltageB, payload.FreqA, payload.FreqB);
    }

    public async Task<List<TelemetryHistoryResponse>> GetHistoryAsync(
        string deviceId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var records = await _telemetryRepository.GetHistoryAsync(deviceId, from, to, limit, cancellationToken);

        return records.Select(r => new TelemetryHistoryResponse
        {
            Id = r.Id,
            DeviceId = r.DeviceId,
            VoltageA = r.VoltageA,
            VoltageB = r.VoltageB,
            FrequencyA = r.FrequencyA,
            FrequencyB = r.FrequencyB,
            DeviceTimestamp = r.DeviceTimestamp,
            ReceivedAt = r.ReceivedAt
        }).ToList();
    }

    /// <summary>
    /// Parse timestamp firmware. Firmware saat ini kirim waktu tanpa timezone; sampai firmware
    /// mengirim ISO 8601 ber-timezone, nilai naive diperlakukan sebagai UTC agar diterima kolom
    /// timestamptz. Gagal parse -> null (received_at tetap jadi acuan urutan yang andal).
    /// </summary>
    private static DateTime? ParseDeviceTimestamp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateTime.TryParseExact(raw, DeviceTimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
        {
            return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }

        return null;
    }
}
