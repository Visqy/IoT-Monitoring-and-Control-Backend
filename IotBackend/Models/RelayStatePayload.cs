namespace IotBackend.Models;

/// <summary>
/// Payload JSON dari topic <c>{deviceId}/relay/state</c> (kondisi aktual, dari ESP). CommandId
/// nullable karena firmware lama mungkin kirim payload string sederhana ("ON"/"OFF") tanpa JSON
/// sama sekali — fallback itu ditangani terpisah di RelayCommandService, bukan lewat kelas ini.
/// </summary>
public sealed class RelayStatePayload
{
    public Guid? CommandId { get; init; }
    public string? State { get; init; }
    public string? ExecutedAt { get; init; }

    /// <summary>
    /// Pemicu perubahan relay dari sisi ESP: "dashboard", "rfid", atau "boot". Nullable karena
    /// firmware lama (payload string polos) tidak mengirimnya. Beda dari <c>source</c> di
    /// relay/set (intent backend) — ini sumber perubahan aktual yang dilaporkan device.
    /// </summary>
    public string? Source { get; init; }
}
