-- 001_initial_schema.sql
-- Schema awal IotBackend. Idempotent (CREATE TABLE IF NOT EXISTS) sehingga aman dijalankan
-- setiap startup oleh DatabaseInitializer. Sumber kebenaran: docs/DATABASE_SCHEMA.md.
--
-- CATATAN: script ini TIDAK melakukan migrasi (tidak ada ALTER TABLE). Kalau tabel sudah ada
-- dengan schema lama, CREATE TABLE IF NOT EXISTS di bawah akan di-skip diam-diam dan kolom
-- baru TIDAK akan muncul. Untuk PoC ini, itu ditangani dengan drop database/tabel lama secara
-- manual sebelum deploy schema baru -- bukan tanggung jawab script ini.

-- Master data device
CREATE TABLE IF NOT EXISTS devices (
    device_id  VARCHAR PRIMARY KEY,
    name       TEXT,
    location   TEXT,
    status     TEXT NOT NULL DEFAULT 'unknown',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Raw history telemetry, satu baris per pesan MQTT masuk
CREATE TABLE IF NOT EXISTS telemetry (
    id        BIGSERIAL PRIMARY KEY,
    device_id VARCHAR NOT NULL,
    topic     TEXT NOT NULL,

    voltage_a   DOUBLE PRECISION,
    voltage_b   DOUBLE PRECISION,
    current_b   DOUBLE PRECISION,
    power_b     DOUBLE PRECISION,
    frequency_b DOUBLE PRECISION,

    device_timestamp TIMESTAMPTZ,
    received_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    raw_payload JSONB NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_telemetry_device_received
ON telemetry (device_id, received_at DESC);

-- Satu baris kondisi terbaru per device -- titik gabung dari 3 sumber (telemetry, relay/state,
-- status/LWT), bukan sekadar cache performa dari tabel telemetry.
CREATE TABLE IF NOT EXISTS device_current_state (
    device_id VARCHAR PRIMARY KEY,

    status      TEXT NOT NULL DEFAULT 'unknown',
    voltage_a   DOUBLE PRECISION,
    voltage_b   DOUBLE PRECISION,
    current_b   DOUBLE PRECISION,
    power_b     DOUBLE PRECISION,
    frequency_b DOUBLE PRECISION,
    relay_state BOOLEAN,

    last_seen  TIMESTAMPTZ,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Tracking semua perubahan relay: command dari dashboard (lifecycle penuh: pending -> sent ->
-- executed/failed/timeout) DAN event device-initiated / RFID-boot (langsung 'executed', tidak
-- pernah 'pending'/'sent'). command_id VARCHAR (bukan UUID native): command dashboard tetap
-- diisi UUID standar, tapi event device-initiated diisi ID sintetis "{source}-{guid}" (mis.
-- "rfid-3fa85f64...") -- lihat docs/DATABASE_SCHEMA.md untuk detail lengkap.
CREATE TABLE IF NOT EXISTS relay_commands (
    command_id VARCHAR PRIMARY KEY,
    device_id  VARCHAR NOT NULL,

    requested_state BOOLEAN NOT NULL,
    actual_state    BOOLEAN,

    source TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'pending',

    requested_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    sent_at         TIMESTAMPTZ,
    acknowledged_at TIMESTAMPTZ,

    error_message TEXT,
    raw_payload   JSONB
);

CREATE INDEX IF NOT EXISTS idx_relay_commands_device_requested
ON relay_commands (device_id, requested_at DESC);
