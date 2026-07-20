namespace IotBackend.Options;

/// <summary>
/// Konfigurasi background sweep deteksi device offline (binding dari section "DeviceOffline").
/// Backstop untuk LWT <c>+/status</c> — lihat docs/DATABASE_SCHEMA.md §"Deteksi online/offline".
/// </summary>
public sealed class DeviceOfflineOptions
{
    public const string SectionName = "DeviceOffline";

    /// <summary>
    /// Batas waktu sejak <c>last_seen</c> sebelum device berstatus <c>online</c> ditandai
    /// <c>offline</c>. Firmware publish tiap 1 menit — disarankan 150-180 detik (docs).
    /// </summary>
    public int OfflineThresholdSeconds { get; init; } = 165;

    /// <summary>Seberapa sering background service memindai device yang basi.</summary>
    public int SweepIntervalSeconds { get; init; } = 30;
}
