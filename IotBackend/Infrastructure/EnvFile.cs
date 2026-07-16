namespace IotBackend.Infrastructure;

/// <summary>
/// Loader .env sederhana untuk development lokal. Membaca file .env (kalau ada) dan menaruh
/// key=value sebagai environment variable proses SEBELUM WebApplication.CreateBuilder
/// dipanggil, supaya environment variable configuration provider bawaan ASP.NET Core bisa
/// membacanya. Tidak menimpa env var yang sudah di-set di level OS/shell (mis. di server
/// produksi) — .env hanya untuk kemudahan development.
/// </summary>
public static class EnvFile
{
    public static void Load(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = trimmed[..separatorIndex].Trim();
            var value = trimmed[(separatorIndex + 1)..].Trim().Trim('"');

            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
