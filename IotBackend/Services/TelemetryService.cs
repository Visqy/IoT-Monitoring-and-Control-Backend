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

    // Firmware kirim waktu lokal WIB tanpa offset (CLAUDE.md §9) -- dipakai untuk konversi ke UTC.
    private static readonly TimeSpan DeviceTimeZoneOffset = TimeSpan.FromHours(7);

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
            CurrentB = payload.CurrentB,
            PowerB = payload.PowerB,
            EnergyB = payload.EnergyB,
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
            CurrentB = payload.CurrentB,
            PowerB = payload.PowerB,
            EnergyB = payload.EnergyB,
            FrequencyB = payload.FreqB,
            LastSeen = receivedAt
        };
        await _deviceStateRepository.UpsertFromTelemetryAsync(state, cancellationToken);

        // Auto-register ke tabel devices (master data) supaya GET /api/devices tidak kosong
        // begitu ada device baru mengirim telemetry. name/location tetap NULL (data manual).
        // status TIDAK ditulis di sini -- device_current_state.status di atas satu-satunya
        // sumber kebenaran (docs/DATABASE_SCHEMA.md "Single writer buat status").
        await _deviceRepository.EnsureRegisteredAsync(deviceId, cancellationToken);

        _logger.LogInformation(
            "Telemetry {DeviceId} tersimpan (Va={Va} Vb={Vb} Ib={Ib} Pb={Pb} Eb={Eb} Fb={Fb}).",
            deviceId, payload.VoltageA, payload.VoltageB, payload.CurrentB, payload.PowerB, payload.EnergyB, payload.FreqB);
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
            CurrentB = r.CurrentB,
            PowerB = r.PowerB,
            EnergyB = r.EnergyB,
            FrequencyB = r.FrequencyB,
            DeviceTimestamp = r.DeviceTimestamp,
            ReceivedAt = r.ReceivedAt
        }).ToList();
    }

    /// <summary>
    /// Parse timestamp firmware. Firmware saat ini kirim waktu lokal WIB tanpa offset; sampai
    /// firmware mengirim ISO 8601 ber-timezone, nilai naive dikonversi eksplisit ke UTC di sini
    /// (bukan ditag Utc langsung -- itu bug -7 jam lama, CLAUDE.md §9). Gagal parse -> null
    /// (received_at tetap jadi acuan urutan yang andal).
    /// </summary>
    private static DateTime? ParseDeviceTimestamp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateTime.TryParseExact(raw, DeviceTimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsedLocal))
        {
            return DateTime.SpecifyKind(parsedLocal - DeviceTimeZoneOffset, DateTimeKind.Utc);
        }

        return null;
    }
}
