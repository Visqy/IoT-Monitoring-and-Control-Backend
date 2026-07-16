namespace IotBackend.Options;

/// <summary>Konfigurasi perilaku command relay (binding dari section "Relay").</summary>
public sealed class RelayOptions
{
    public const string SectionName = "Relay";

    /// <summary>
    /// Batas waktu sebuah command berstatus <c>sent</c> menunggu konfirmasi <c>relay/state</c>
    /// sebelum ditandai <c>timeout</c> (lihat state machine di docs/DATABASE_SCHEMA.md).
    /// </summary>
    public int CommandTimeoutSeconds { get; init; } = 30;

    /// <summary>Seberapa sering background service memindai command yang kedaluwarsa.</summary>
    public int TimeoutScanIntervalSeconds { get; init; } = 10;
}
