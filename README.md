# IoT Monitoring and Control Backend

Backend ASP.NET Core (.NET 10) untuk proyek IoT PoC: menerima telemetry listrik (PZEM) dari
device ESP32 via MQTT, menyimpannya ke PostgreSQL, dan menyediakan REST API untuk membaca data
serta mengirim command relay ON/OFF.

**Status:** Proof of Concept (PoC) - belum production-ready.

---

## Arsitektur singkat

```
ESP32 (PZEM + RFID + Relay)
    | MQTT / TLS
    v
HiveMQ Cloud
    |
    v
ASP.NET Core Backend (MqttSubscriberService) --> PostgreSQL (Supabase / lokal)
    ^
    | REST API
Dashboard (fase berikutnya)
```

Backend berbentuk **modular monolith**, controller-based Web API, dengan lapisan
`Controller -> Service -> Repository` dan akses PostgreSQL manual via Npgsql (tanpa EF Core).

## Stack

| Bagian | Teknologi |
|---|---|
| Runtime | .NET 10 |
| Gaya API | Controller-based Web API |
| MQTT broker | HiveMQ Cloud (TLS) |
| MQTT client | MQTTnet |
| Database | PostgreSQL (lokal atau Supabase) |
| DB driver | Npgsql, SQL parameterized manual |
| API docs | OpenAPI + Scalar (`/scalar/v1`) |

## Struktur proyek

```
IotBackend/
|-- Controllers/         DevicesController, TelemetryController, RelayController, HealthController
|-- BackgroundServices/  MqttSubscriberService, RelayCommandTimeoutService
|-- Services/            TelemetryService, RelayCommandService, DeviceService
|-- Repositories/        TelemetryRepository, DeviceStateRepository, RelayCommandRepository, DeviceRepository
|-- Models/              Representasi internal / payload MQTT
|-- Contracts/           DTO response API
|-- Infrastructure/      MqttClientService, DatabaseInitializer, EnvFile
|-- Options/             MqttOptions, RelayOptions
|-- Database/            001_initial_schema.sql
`-- Program.cs
```

## Menjalankan proyek

### Prasyarat
- .NET 10 SDK
- PostgreSQL (lokal) atau project Supabase
- Akun/cluster HiveMQ Cloud

### 1. Isi konfigurasi

Kredensial **wajib** lewat User Secrets atau `.env` - jangan pernah ditulis ke `appsettings.json`
atau commit ke Git.

**Opsi A - User Secrets:**
```powershell
cd IotBackend
dotnet user-secrets init
dotnet user-secrets set "Mqtt:Host" "..."
dotnet user-secrets set "Mqtt:Username" "..."
dotnet user-secrets set "Mqtt:Password" "..."
dotnet user-secrets set "ConnectionStrings:Postgres" "..."
```
Lihat `add_user_secrets_commands.txt` untuk contoh lengkap.

**Opsi B - file `.env`:**
```powershell
cd IotBackend
copy .env.example .env
# lalu isi nilai asli di .env
```

### 2. Jalankan

```powershell
dotnet build
dotnet run --project IotBackend
```

Schema database dibuat otomatis saat startup (`DatabaseInitializer`). Swagger/Scalar UI tersedia
di `/scalar/v1` saat development.

## REST API

| Method | Route | Fungsi |
|---|---|---|
| GET | `/api/health` | Health check (per-dependency) — publik, tidak butuh token |
| POST | `/api/auth/login` | Login (satu akun bersama) — publik, return JWT |
| GET | `/api/devices` | List device terdaftar — butuh `Authorization: Bearer <token>` |
| GET | `/api/devices/{deviceId}/state` | Kondisi terbaru device — butuh token |
| GET | `/api/devices/{deviceId}/telemetry` | Riwayat telemetry — butuh token |
| POST | `/api/devices/{deviceId}/relay` | Kirim command relay (202 Accepted) — butuh token |
| GET | `/api/commands/{commandId}` | Status command relay — butuh token |
| GET | `/api/devices/{deviceId}/relay-commands` | Riwayat perubahan relay per device (dashboard + RFID/boot) — butuh token |
| GET | `/api/rfid-cards` | Daftar whitelist kartu RFID (global) — butuh token |
| POST | `/api/rfid-cards` | Tambah kartu ke whitelist — butuh token |
| PATCH | `/api/rfid-cards/{uid}` | Aktif/nonaktifkan atau ubah label kartu — butuh token |
| DELETE | `/api/rfid-cards/{uid}` | Hapus kartu dari whitelist — butuh token |
| GET | `/api/rfid-events` | Riwayat scan kartu (termasuk yang ditolak) — butuh token |
| GET | `/api/stream` | Push realtime (SSE, event `device-state` & `rfid-scan`) — butuh token |

## Catatan

Proyek ini adalah sisi **backend**. Firmware device (ESP32 + PZEM + RFID + Relay) berada di
repo/folder terpisah dan berkomunikasi lewat MQTT sesuai kontrak topic `pzem`, `status`,
`relay/set`, dan `relay/state`.