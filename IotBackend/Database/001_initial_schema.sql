-- 001_initial_schema.sql
-- Schema awal IotBackend. Idempotent (CREATE TABLE IF NOT EXISTS) sehingga aman dijalankan
-- setiap startup oleh DatabaseInitializer. Sumber kebenaran: docs/DATABASE_SCHEMA.md.

-- Master data device
CREATE TABLE IF NOT EXISTS devices (
    device_id  TEXT PRIMARY KEY,
    name       TEXT,
    location   TEXT,
    status     TEXT NOT NULL DEFAULT 'unknown',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Raw history telemetry, satu baris per pesan MQTT masuk
CREATE TABLE IF NOT EXISTS telemetry (
    id          BIGSERIAL PRIMARY KEY,
    device_id   TEXT NOT NULL,
    topic       TEXT NOT NULL,

    voltage_a   DOUBLE PRECISION,
    voltage_b   DOUBLE PRECISION,
    frequency_a DOUBLE PRECISION,
    frequency_b DOUBLE PRECISION,

    device_timestamp TIMESTAMPTZ,
    received_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    raw_payload JSONB NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_telemetry_device_received
ON telemetry (device_id, received_at DESC);

-- Satu baris kondisi terbaru per device (dashboard tidak perlu scan tabel telemetry)
CREATE TABLE IF NOT EXISTS device_current_state (
    device_id   TEXT PRIMARY KEY,

    status      TEXT NOT NULL DEFAULT 'unknown',
    voltage_a   DOUBLE PRECISION,
    voltage_b   DOUBLE PRECISION,
    frequency_a DOUBLE PRECISION,
    frequency_b DOUBLE PRECISION,
    relay_state BOOLEAN,

    last_seen  TIMESTAMPTZ,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Tracking command relay dari dashboard ke ESP, termasuk konfirmasi eksekusi aktual
CREATE TABLE IF NOT EXISTS relay_commands (
    command_id UUID PRIMARY KEY,
    device_id  TEXT NOT NULL,

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
