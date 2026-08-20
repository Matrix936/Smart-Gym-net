-- ============================================================================
-- Smart Gym — Schema SQLite v2 (FK reales)
-- ============================================================================
-- Fuente de verdad: docs/arquitectura/02-modelo-datos-smart-gym.md
--                   docs/arquitectura/03-sincronizacion-smart-gym.md
--                   docs/arquitectura/00-decisiones-tecnicas-smart-gym.md
--
-- CAMBIO DE DECISIÓN respecto a v1: Ferre-POS gestiona relaciones solo en
-- código (sin FK nativas en SQLite) por deuda técnica heredada de un proyecto
-- que ya estaba en producción. Smart Gym nace limpio, así que SÍ se declaran
-- FOREIGN KEY reales desde el inicio — vale más que SQLite detecte un
-- id_socio huérfano en desarrollo que la flexibilidad que le sirvió a
-- Ferre-POS. Requiere `PRAGMA foreign_keys = ON` en cada conexión (ya
-- configurado en db.rs / create_sqlite_connection).
--
-- Convenciones:
--   - snake_case en tablas y columnas
--   - Prefijo id_ en columnas FK, y la FK declarada apunta exactamente a la
--     PK de la tabla referenciada (ver cada CREATE TABLE).
--   - Dinero SIEMPRE como INTEGER, sufijo _centavos. Nunca REAL/FLOAT.
--   - IDs: INTEGER AUTOINCREMENT para catálogos, TEXT (UUID v4) para
--     transaccionales.
--   - Sin ON DELETE CASCADE por defecto (comportamiento NO ACTION / RESTRICT
--     de SQLite): un borrado físico que dejaría huérfanos se rechaza en vez
--     de propagarse en cascada silenciosamente — dado que casi todo usa soft
--     delete (deleted_at), un hard delete real debe ser un caso excepcional
--     y explícito, no algo que ocurra por arrastre.
--   - Las tablas están creadas en orden de dependencia: toda tabla referenciada
--     se crea ANTES que la tabla que la referencia. Si agregas una tabla
--     nueva, respeta este orden o SQLite rechazará el CREATE TABLE.
--   - Tablas NO sincronizables (socios_biometricos, perifericos_config,
--     sesiones, cuentas_recordadas_local) NO llevan columnas de sync —
--     excluidas por diseño del worker de sync (ver 03-sincronizacion-smart-gym.md §2).
-- ============================================================================

PRAGMA foreign_keys = ON;

-- ============================================================================
-- 1. SEGURIDAD Y CONFIGURACIÓN
-- ============================================================================

CREATE TABLE IF NOT EXISTS roles (
    id_rol      INTEGER PRIMARY KEY AUTOINCREMENT,
    nombre      TEXT NOT NULL UNIQUE,
    descripcion TEXT,
    created_at  TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
);

CREATE TABLE IF NOT EXISTS permisos_rol (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    id_rol      INTEGER NOT NULL,
    accion      TEXT NOT NULL,
    created_at  TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    UNIQUE (id_rol, accion),
    FOREIGN KEY (id_rol) REFERENCES roles(id_rol)
);
CREATE INDEX IF NOT EXISTS idx_permisos_rol_id_rol ON permisos_rol(id_rol);

CREATE TABLE IF NOT EXISTS sedes (
    id_sede           INTEGER PRIMARY KEY AUTOINCREMENT,
    nombre            TEXT NOT NULL,
    direccion         TEXT,
    telefono          TEXT,
    horario_apertura  TEXT,
    horario_cierre    TEXT,
    es_activa         INTEGER NOT NULL DEFAULT 1,
    updated_at        TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    sincronizado      INTEGER NOT NULL DEFAULT 0,
    deleted_at        TEXT
);

CREATE TABLE IF NOT EXISTS usuarios (
    id_usuario      INTEGER PRIMARY KEY AUTOINCREMENT,
    nombre          TEXT NOT NULL,
    apellido_paterno TEXT NOT NULL DEFAULT '',
    apellido_materno TEXT NOT NULL DEFAULT '',
    email           TEXT NOT NULL UNIQUE,
    password_hash   TEXT NOT NULL,
    id_rol          INTEGER NOT NULL,
    id_sede         INTEGER, -- NULL = acceso global (SUPERADMIN)
    es_activo       INTEGER NOT NULL DEFAULT 1,
    updated_at      TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    sincronizado    INTEGER NOT NULL DEFAULT 0,
    deleted_at      TEXT,
    created_at      TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    FOREIGN KEY (id_rol) REFERENCES roles(id_rol),
    FOREIGN KEY (id_sede) REFERENCES sedes(id_sede)
);
CREATE INDEX IF NOT EXISTS idx_usuarios_id_rol ON usuarios(id_rol);
CREATE INDEX IF NOT EXISTS idx_usuarios_id_sede ON usuarios(id_sede);

-- NO sincronizable (03-sincronizacion §2). Estado de autenticación local.
CREATE TABLE IF NOT EXISTS sesiones (
    id_sesion   TEXT PRIMARY KEY,
    id_usuario  INTEGER NOT NULL,
    token_hash  TEXT NOT NULL,
    created_at  TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    expires_at  TEXT NOT NULL,
    revoked_at  TEXT,
    FOREIGN KEY (id_usuario) REFERENCES usuarios(id_usuario)
);
CREATE INDEX IF NOT EXISTS idx_sesiones_id_usuario ON sesiones(id_usuario);

-- NO sincronizable. Config de hardware específico de esta terminal.
CREATE TABLE IF NOT EXISTS perifericos_config (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    tipo                TEXT NOT NULL, -- 'impresora_ticket' | 'impresora_etiqueta' | 'lector_huella'
    nombre_dispositivo  TEXT NOT NULL,
    config_json         TEXT,
    es_predeterminado   INTEGER NOT NULL DEFAULT 0,
    created_at          TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    updated_at          TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
);

-- NO sincronizable. Cuentas de usuario recordadas localmente para autocomplete
-- en la pantalla de login. Solo almacena datos de display (nombre + email),
-- nunca contraseñas ni tokens.
CREATE TABLE IF NOT EXISTS cuentas_recordadas_local (
    id_usuario      INTEGER PRIMARY KEY,
    nombre          TEXT NOT NULL,
    email           TEXT NOT NULL UNIQUE,
    ultimo_login    TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
);

CREATE TABLE IF NOT EXISTS configuracion_general (
    clave       TEXT PRIMARY KEY,
    valor       TEXT,
    updated_at  TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
);

CREATE TABLE IF NOT EXISTS empresa_config_fiscal (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    nombre_comercial TEXT NOT NULL,
    telefono        TEXT NOT NULL,
    direccion       TEXT NOT NULL,
    codigo_postal   TEXT NOT NULL,
    razon_social    TEXT,
    rfc             TEXT,
    regimen_fiscal  TEXT,
    logo_path       TEXT,
    updated_at      TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    sincronizado    INTEGER NOT NULL DEFAULT 0,
    deleted_at      TEXT
);

-- ============================================================================
-- 2. SOCIOS
-- ============================================================================

CREATE TABLE IF NOT EXISTS socios (
    id_socio                     TEXT PRIMARY KEY,
    nombre                       TEXT NOT NULL,
    apellido_paterno             TEXT NOT NULL DEFAULT '',
    apellido_materno             TEXT NOT NULL DEFAULT '',
    email                        TEXT,
    telefono                     TEXT,
    fecha_nacimiento             TEXT,
    id_sede_registro             INTEGER NOT NULL,
    foto_path                    TEXT,
    contacto_emergencia_nombre   TEXT,
    contacto_emergencia_telefono TEXT,
    estado                       TEXT NOT NULL DEFAULT 'activo',
    fecha_ultimo_acceso          TEXT,
    updated_at                   TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    sincronizado                 INTEGER NOT NULL DEFAULT 0,
    deleted_at                   TEXT,
    created_at                   TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    FOREIGN KEY (id_sede_registro) REFERENCES sedes(id_sede)
);
CREATE INDEX IF NOT EXISTS idx_socios_estado ON socios(estado);
CREATE INDEX IF NOT EXISTS idx_socios_id_sede_registro ON socios(id_sede_registro);

-- NO sincronizable (03-sincronizacion §2). Template gestionado por el sidecar
-- biométrico (DigitalPersona), nunca sale del equipo.
CREATE TABLE IF NOT EXISTS socios_biometricos (
    id_registro             TEXT PRIMARY KEY,
    id_socio                TEXT NOT NULL,
    dedo                    TEXT NOT NULL,
    archivo_template_path   TEXT NOT NULL,
    algoritmo_sdk           TEXT,
    es_activa               INTEGER NOT NULL DEFAULT 1,
    created_at              TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    deleted_at              TEXT,
    FOREIGN KEY (id_socio) REFERENCES socios(id_socio)
);
CREATE INDEX IF NOT EXISTS idx_socios_biometricos_id_socio ON socios_biometricos(id_socio);

CREATE TABLE IF NOT EXISTS socios_historial_estado (
    id              TEXT PRIMARY KEY,
    id_socio        TEXT NOT NULL,
    estado_anterior TEXT,
    estado_nuevo    TEXT NOT NULL,
    motivo          TEXT,
    id_usuario      INTEGER,
    created_at      TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    updated_at      TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    sincronizado    INTEGER NOT NULL DEFAULT 0,
    deleted_at      TEXT,
    FOREIGN KEY (id_socio) REFERENCES socios(id_socio),
    FOREIGN KEY (id_usuario) REFERENCES usuarios(id_usuario)
);
CREATE INDEX IF NOT EXISTS idx_socios_historial_estado_id_socio ON socios_historial_estado(id_socio);

-- ============================================================================
-- 3. CATÁLOGOS INDEPENDIENTES (sin FK, requeridos antes de tablas dependientes)
-- ============================================================================

CREATE TABLE IF NOT EXISTS planes_membresia (
    id_plan                 INTEGER PRIMARY KEY AUTOINCREMENT,
    nombre                  TEXT NOT NULL,
    descripcion             TEXT,
    dias_vigencia           INTEGER NOT NULL,
    dias_congelamiento_max  INTEGER NOT NULL DEFAULT 0,
    precio_centavos         INTEGER NOT NULL,
    es_activo               INTEGER NOT NULL DEFAULT 1,
    updated_at              TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    sincronizado            INTEGER NOT NULL DEFAULT 0,
    deleted_at              TEXT
);

CREATE TABLE IF NOT EXISTS dispositivos_acceso (
    id_dispositivo  INTEGER PRIMARY KEY AUTOINCREMENT,
    nombre          TEXT NOT NULL,
    tipo            TEXT NOT NULL, -- biometrico | manual
    id_sede         INTEGER NOT NULL,
    es_activo       INTEGER NOT NULL DEFAULT 1,
    updated_at      TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    sincronizado    INTEGER NOT NULL DEFAULT 0,
    deleted_at      TEXT,
    FOREIGN KEY (id_sede) REFERENCES sedes(id_sede)
);

CREATE TABLE IF NOT EXISTS categorias_productos (
    id_categoria  INTEGER PRIMARY KEY AUTOINCREMENT,
    nombre        TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS productos (
    id_producto             INTEGER PRIMARY KEY AUTOINCREMENT,
    codigo_barras           TEXT UNIQUE,
    descripcion             TEXT NOT NULL,
    precio_venta_centavos   INTEGER NOT NULL,
    costo_promedio_centavos INTEGER NOT NULL DEFAULT 0,
    id_categoria            INTEGER,
    requiere_inventario     INTEGER NOT NULL DEFAULT 1,
    es_activo               INTEGER NOT NULL DEFAULT 1,
    updated_at              TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    sincronizado            INTEGER NOT NULL DEFAULT 0,
    deleted_at              TEXT,
    FOREIGN KEY (id_categoria) REFERENCES categorias_productos(id_categoria)
);
CREATE INDEX IF NOT EXISTS idx_productos_codigo_barras ON productos(codigo_barras);

CREATE TABLE IF NOT EXISTS inventario_sucursal (
    id_producto   INTEGER NOT NULL,
    id_sede       INTEGER NOT NULL,
    stock         INTEGER NOT NULL DEFAULT 0,
    stock_minimo  INTEGER NOT NULL DEFAULT 0,
    updated_at    TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    sincronizado  INTEGER NOT NULL DEFAULT 0,
    deleted_at    TEXT,
    PRIMARY KEY (id_producto, id_sede),
    FOREIGN KEY (id_producto) REFERENCES productos(id_producto),
    FOREIGN KEY (id_sede) REFERENCES sedes(id_sede)
);

-- ============================================================================
-- 4. CAJA (debe existir antes de membresias_pagos y ventas, que la referencian)
-- ============================================================================

CREATE TABLE IF NOT EXISTS cajas_sesiones (
    id_sesion                 TEXT PRIMARY KEY,
    id_usuario                INTEGER NOT NULL,
    id_sede                   INTEGER NOT NULL,
    monto_inicial_centavos    INTEGER NOT NULL,
    monto_final_centavos      INTEGER,
    monto_esperado_centavos   INTEGER,
    fecha_apertura            TEXT NOT NULL,
    fecha_cierre              TEXT,
    estado                    TEXT NOT NULL DEFAULT 'abierta', -- abierta | cerrada
    updated_at                TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    sincronizado              INTEGER NOT NULL DEFAULT 0,
    deleted_at                TEXT,
    FOREIGN KEY (id_usuario) REFERENCES usuarios(id_usuario),
    FOREIGN KEY (id_sede) REFERENCES sedes(id_sede)
);
CREATE INDEX IF NOT EXISTS idx_cajas_sesiones_id_usuario ON cajas_sesiones(id_usuario);
CREATE INDEX IF NOT EXISTS idx_cajas_sesiones_estado ON cajas_sesiones(estado);

CREATE TABLE IF NOT EXISTS caja_movimientos (
    id_movimiento     TEXT PRIMARY KEY,
    id_sesion         TEXT NOT NULL,
    tipo              TEXT NOT NULL, -- ingreso | egreso
    concepto          TEXT,
    monto_centavos    INTEGER NOT NULL,
    metodo_pago       TEXT NOT NULL,
    afecta_efectivo   INTEGER NOT NULL DEFAULT 1,
    referencia_tipo   TEXT, -- 'venta' | 'pago_membresia' — polimórfico, NO es FK real
    referencia_id     TEXT, -- id de ventas.id_venta o membresias_pagos.id_pago según referencia_tipo
    created_at        TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    updated_at        TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    sincronizado      INTEGER NOT NULL DEFAULT 0,
    deleted_at        TEXT,
    FOREIGN KEY (id_sesion) REFERENCES cajas_sesiones(id_sesion)
);
CREATE INDEX IF NOT EXISTS idx_caja_movimientos_id_sesion ON caja_movimientos(id_sesion);

-- ============================================================================
-- 5. MEMBRESÍAS
-- ============================================================================

CREATE TABLE IF NOT EXISTS membresias (
    id_membresia        TEXT PRIMARY KEY,
    id_socio             TEXT NOT NULL,
    id_plan               INTEGER NOT NULL,
    id_sede                INTEGER NOT NULL,
    fecha_inicio            TEXT NOT NULL,
    fecha_fin                 TEXT NOT NULL,
    fecha_cancelacion          TEXT,
    estado                       TEXT NOT NULL DEFAULT 'activa', -- activa | vencida | congelada | cancelada
    id_vendedor                    INTEGER,
    updated_at                       TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    sincronizado                      INTEGER NOT NULL DEFAULT 0,
    deleted_at                         TEXT,
    created_at                          TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    FOREIGN KEY (id_socio) REFERENCES socios(id_socio),
    FOREIGN KEY (id_plan) REFERENCES planes_membresia(id_plan),
    FOREIGN KEY (id_sede) REFERENCES sedes(id_sede),
    FOREIGN KEY (id_vendedor) REFERENCES usuarios(id_usuario)
);
CREATE INDEX IF NOT EXISTS idx_membresias_id_socio ON membresias(id_socio);
CREATE INDEX IF NOT EXISTS idx_membresias_estado ON membresias(estado);
CREATE INDEX IF NOT EXISTS idx_membresias_fecha_fin ON membresias(fecha_fin);

CREATE TABLE IF NOT EXISTS membresias_pagos (
    id_pago             TEXT PRIMARY KEY,
    id_membresia         TEXT NOT NULL,
    monto_centavos          INTEGER NOT NULL,
    metodo_pago                TEXT NOT NULL,
    referencia_pago                TEXT,
    fecha_pago                       TEXT NOT NULL,
    id_caja_movimiento                  TEXT,
    id_vendedor                            INTEGER,
    updated_at                                TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    sincronizado                                INTEGER NOT NULL DEFAULT 0,
    deleted_at                                   TEXT,
    FOREIGN KEY (id_membresia) REFERENCES membresias(id_membresia),
    FOREIGN KEY (id_caja_movimiento) REFERENCES caja_movimientos(id_movimiento),
    FOREIGN KEY (id_vendedor) REFERENCES usuarios(id_usuario)
);
CREATE INDEX IF NOT EXISTS idx_membresias_pagos_id_membresia ON membresias_pagos(id_membresia);

CREATE TABLE IF NOT EXISTS membresias_congelamientos (
    id              TEXT PRIMARY KEY,
    id_membresia    TEXT NOT NULL,
    fecha_inicio    TEXT NOT NULL,
    fecha_fin       TEXT NOT NULL,
    motivo          TEXT,
    autorizado_por  INTEGER,
    updated_at      TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    sincronizado    INTEGER NOT NULL DEFAULT 0,
    deleted_at      TEXT,
    FOREIGN KEY (id_membresia) REFERENCES membresias(id_membresia),
    FOREIGN KEY (autorizado_por) REFERENCES usuarios(id_usuario)
);
CREATE INDEX IF NOT EXISTS idx_membresias_congelamientos_id_membresia ON membresias_congelamientos(id_membresia);

-- ============================================================================
-- 6. CONTROL DE ACCESO
-- ============================================================================

CREATE TABLE IF NOT EXISTS accesos_bitacora (
    id_acceso            TEXT PRIMARY KEY,
    id_socio               TEXT,
    id_sede                  INTEGER NOT NULL,
    timestamp                  TEXT NOT NULL,
    tipo                         TEXT NOT NULL, -- entrada | salida
    metodo                         TEXT NOT NULL, -- huella | manual
    id_dispositivo                    INTEGER,
    estado                              TEXT NOT NULL, -- concedido | denegado
    motivo_denegacion                     TEXT,
    updated_at                              TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    sincronizado                              INTEGER NOT NULL DEFAULT 0,
    deleted_at                                 TEXT,
    FOREIGN KEY (id_socio) REFERENCES socios(id_socio),
    FOREIGN KEY (id_sede) REFERENCES sedes(id_sede),
    FOREIGN KEY (id_dispositivo) REFERENCES dispositivos_acceso(id_dispositivo)
);
CREATE INDEX IF NOT EXISTS idx_accesos_bitacora_id_socio ON accesos_bitacora(id_socio);
CREATE INDEX IF NOT EXISTS idx_accesos_bitacora_timestamp ON accesos_bitacora(timestamp);
CREATE INDEX IF NOT EXISTS idx_accesos_bitacora_id_sede ON accesos_bitacora(id_sede);

-- ============================================================================
-- 7. POS / VENTAS
-- ============================================================================

CREATE TABLE IF NOT EXISTS ventas (
    id_venta             TEXT PRIMARY KEY,
    id_socio               TEXT,
    id_sede                  INTEGER NOT NULL,
    total_centavos             INTEGER NOT NULL, -- calculado server-side, nunca por frontend
    metodo_pago                  TEXT NOT NULL,
    id_caja_movimiento              TEXT,
    id_vendedor                        INTEGER,
    estado                                TEXT NOT NULL DEFAULT 'completada', -- completada | cancelada
    updated_at                              TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    sincronizado                              INTEGER NOT NULL DEFAULT 0,
    deleted_at                                 TEXT,
    created_at                                  TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    FOREIGN KEY (id_socio) REFERENCES socios(id_socio),
    FOREIGN KEY (id_sede) REFERENCES sedes(id_sede),
    FOREIGN KEY (id_caja_movimiento) REFERENCES caja_movimientos(id_movimiento),
    FOREIGN KEY (id_vendedor) REFERENCES usuarios(id_usuario)
);
CREATE INDEX IF NOT EXISTS idx_ventas_id_socio ON ventas(id_socio);
CREATE INDEX IF NOT EXISTS idx_ventas_id_sede ON ventas(id_sede);

CREATE TABLE IF NOT EXISTS detalle_ventas (
    id_detalle                TEXT PRIMARY KEY,
    id_venta                    TEXT NOT NULL,
    id_producto                    INTEGER NOT NULL,
    cantidad                          INTEGER NOT NULL,
    precio_unitario_centavos             INTEGER NOT NULL,
    subtotal_centavos                       INTEGER NOT NULL,
    updated_at                                 TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    sincronizado                                 INTEGER NOT NULL DEFAULT 0,
    deleted_at                                    TEXT,
    FOREIGN KEY (id_venta) REFERENCES ventas(id_venta),
    FOREIGN KEY (id_producto) REFERENCES productos(id_producto)
);
CREATE INDEX IF NOT EXISTS idx_detalle_ventas_id_venta ON detalle_ventas(id_venta);

-- ============================================================================
-- 8. COBRANZA
-- ============================================================================

CREATE TABLE IF NOT EXISTS cuentas_cobrar (
    id_cuenta                  TEXT PRIMARY KEY,
    id_membresia                  TEXT NOT NULL,
    id_socio                        TEXT NOT NULL,
    saldo_pendiente_centavos          INTEGER NOT NULL,
    fecha_vencimiento                    TEXT NOT NULL,
    estado                                  TEXT NOT NULL DEFAULT 'pendiente', -- pendiente | parcial | cobrada | incobrable
    updated_at                                TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    sincronizado                                INTEGER NOT NULL DEFAULT 0,
    deleted_at                                   TEXT,
    FOREIGN KEY (id_membresia) REFERENCES membresias(id_membresia),
    FOREIGN KEY (id_socio) REFERENCES socios(id_socio)
);
CREATE INDEX IF NOT EXISTS idx_cuentas_cobrar_id_socio ON cuentas_cobrar(id_socio);
CREATE INDEX IF NOT EXISTS idx_cuentas_cobrar_estado ON cuentas_cobrar(estado);

CREATE TABLE IF NOT EXISTS cobros_cuotas (
    id_cobro          TEXT PRIMARY KEY,
    id_cuenta            TEXT NOT NULL,
    monto_centavos          INTEGER NOT NULL,
    metodo_pago                TEXT NOT NULL,
    fecha_cobro                   TEXT NOT NULL,
    id_cobrador                      INTEGER,
    resultado                          TEXT NOT NULL DEFAULT 'pendiente', -- exitoso | rechazado | pendiente
    updated_at                            TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    sincronizado                            INTEGER NOT NULL DEFAULT 0,
    deleted_at                               TEXT,
    FOREIGN KEY (id_cuenta) REFERENCES cuentas_cobrar(id_cuenta),
    FOREIGN KEY (id_cobrador) REFERENCES usuarios(id_usuario)
);
CREATE INDEX IF NOT EXISTS idx_cobros_cuotas_id_cuenta ON cobros_cuotas(id_cuenta);

CREATE TABLE IF NOT EXISTS cobros_recordatorios (
    id_recordatorio    TEXT PRIMARY KEY,
    id_socio              TEXT NOT NULL,
    tipo                     TEXT NOT NULL, -- email | whatsapp | sms
    fecha_envio                 TEXT NOT NULL,
    resultado                     TEXT NOT NULL DEFAULT 'enviado', -- enviado | fallido
    updated_at                       TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    sincronizado                       INTEGER NOT NULL DEFAULT 0,
    deleted_at                          TEXT,
    FOREIGN KEY (id_socio) REFERENCES socios(id_socio)
);
CREATE INDEX IF NOT EXISTS idx_cobros_recordatorios_id_socio ON cobros_recordatorios(id_socio);

-- ============================================================================
-- 9. AUDITORÍA
-- ============================================================================

CREATE TABLE IF NOT EXISTS bitacora_auditoria (
    id_registro            TEXT PRIMARY KEY,
    id_usuario                INTEGER,
    accion                       TEXT NOT NULL,
    tabla_afectada                  TEXT NOT NULL,
    id_registro_afectado               TEXT, -- referencia polimórfica, NO es FK real
    valor_anterior                        TEXT, -- JSON
    valor_nuevo                             TEXT, -- JSON
    id_sede                                   INTEGER,
    created_at                                  TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    updated_at                                    TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    sincronizado                                    INTEGER NOT NULL DEFAULT 0,
    deleted_at                                       TEXT,
    FOREIGN KEY (id_usuario) REFERENCES usuarios(id_usuario),
    FOREIGN KEY (id_sede) REFERENCES sedes(id_sede)
);
CREATE INDEX IF NOT EXISTS idx_bitacora_auditoria_tabla_afectada ON bitacora_auditoria(tabla_afectada);
CREATE INDEX IF NOT EXISTS idx_bitacora_auditoria_id_usuario ON bitacora_auditoria(id_usuario);
CREATE INDEX IF NOT EXISTS idx_bitacora_auditoria_created_at ON bitacora_auditoria(created_at);

-- ============================================================================
-- 10. SCHEMA MIGRATIONS — marcador para backfills one-shot
-- ============================================================================

CREATE TABLE IF NOT EXISTS schema_migrations (
    id           TEXT PRIMARY KEY, -- identificador único, ej. '2026_07_backfill_x'
    descripcion  TEXT,
    applied_at   TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
);

-- ============================================================================
-- 11. TRIGGERS — actualización automática de updated_at
-- ============================================================================
-- Un trigger por tabla sincronizable. Las tablas local-only (sesiones,
-- perifericos_config, socios_biometricos, cuentas_recordadas_local)
-- NO llevan trigger de este tipo.
-- ============================================================================

CREATE TRIGGER IF NOT EXISTS trg_sedes_updated_at AFTER UPDATE ON sedes
BEGIN
    UPDATE sedes SET updated_at = strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE id_sede = NEW.id_sede;
END;

CREATE TRIGGER IF NOT EXISTS trg_usuarios_updated_at AFTER UPDATE ON usuarios
BEGIN
    UPDATE usuarios SET updated_at = strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE id_usuario = NEW.id_usuario;
END;

CREATE TRIGGER IF NOT EXISTS trg_socios_updated_at AFTER UPDATE ON socios
BEGIN
    UPDATE socios SET updated_at = strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE id_socio = NEW.id_socio;
END;

CREATE TRIGGER IF NOT EXISTS trg_socios_historial_estado_updated_at AFTER UPDATE ON socios_historial_estado
BEGIN
    UPDATE socios_historial_estado SET updated_at = strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE id = NEW.id;
END;

CREATE TRIGGER IF NOT EXISTS trg_planes_membresia_updated_at AFTER UPDATE ON planes_membresia
BEGIN
    UPDATE planes_membresia SET updated_at = strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE id_plan = NEW.id_plan;
END;

CREATE TRIGGER IF NOT EXISTS trg_dispositivos_acceso_updated_at AFTER UPDATE ON dispositivos_acceso
BEGIN
    UPDATE dispositivos_acceso SET updated_at = strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE id_dispositivo = NEW.id_dispositivo;
END;

CREATE TRIGGER IF NOT EXISTS trg_productos_updated_at AFTER UPDATE ON productos
BEGIN
    UPDATE productos SET updated_at = strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE id_producto = NEW.id_producto;
END;

CREATE TRIGGER IF NOT EXISTS trg_inventario_sucursal_updated_at AFTER UPDATE ON inventario_sucursal
BEGIN
    UPDATE inventario_sucursal SET updated_at = strftime('%Y-%m-%dT%H:%M:%fZ','now')
      WHERE id_producto = NEW.id_producto AND id_sede = NEW.id_sede;
END;

CREATE TRIGGER IF NOT EXISTS trg_cajas_sesiones_updated_at AFTER UPDATE ON cajas_sesiones
BEGIN
    UPDATE cajas_sesiones SET updated_at = strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE id_sesion = NEW.id_sesion;
END;

CREATE TRIGGER IF NOT EXISTS trg_caja_movimientos_updated_at AFTER UPDATE ON caja_movimientos
BEGIN
    UPDATE caja_movimientos SET updated_at = strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE id_movimiento = NEW.id_movimiento;
END;

CREATE TRIGGER IF NOT EXISTS trg_membresias_updated_at AFTER UPDATE ON membresias
BEGIN
    UPDATE membresias SET updated_at = strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE id_membresia = NEW.id_membresia;
END;

CREATE TRIGGER IF NOT EXISTS trg_membresias_pagos_updated_at AFTER UPDATE ON membresias_pagos
BEGIN
    UPDATE membresias_pagos SET updated_at = strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE id_pago = NEW.id_pago;
END;

CREATE TRIGGER IF NOT EXISTS trg_membresias_congelamientos_updated_at AFTER UPDATE ON membresias_congelamientos
BEGIN
    UPDATE membresias_congelamientos SET updated_at = strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE id = NEW.id;
END;

CREATE TRIGGER IF NOT EXISTS trg_accesos_bitacora_updated_at AFTER UPDATE ON accesos_bitacora
BEGIN
    UPDATE accesos_bitacora SET updated_at = strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE id_acceso = NEW.id_acceso;
END;

CREATE TRIGGER IF NOT EXISTS trg_ventas_updated_at AFTER UPDATE ON ventas
BEGIN
    UPDATE ventas SET updated_at = strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE id_venta = NEW.id_venta;
END;

CREATE TRIGGER IF NOT EXISTS trg_detalle_ventas_updated_at AFTER UPDATE ON detalle_ventas
BEGIN
    UPDATE detalle_ventas SET updated_at = strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE id_detalle = NEW.id_detalle;
END;

CREATE TRIGGER IF NOT EXISTS trg_cuentas_cobrar_updated_at AFTER UPDATE ON cuentas_cobrar
BEGIN
    UPDATE cuentas_cobrar SET updated_at = strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE id_cuenta = NEW.id_cuenta;
END;

CREATE TRIGGER IF NOT EXISTS trg_cobros_cuotas_updated_at AFTER UPDATE ON cobros_cuotas
BEGIN
    UPDATE cobros_cuotas SET updated_at = strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE id_cobro = NEW.id_cobro;
END;

CREATE TRIGGER IF NOT EXISTS trg_cobros_recordatorios_updated_at AFTER UPDATE ON cobros_recordatorios
BEGIN
    UPDATE cobros_recordatorios SET updated_at = strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE id_recordatorio = NEW.id_recordatorio;
END;

CREATE TRIGGER IF NOT EXISTS trg_bitacora_auditoria_updated_at AFTER UPDATE ON bitacora_auditoria
BEGIN
    UPDATE bitacora_auditoria SET updated_at = strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE id_registro = NEW.id_registro;
END;

-- ============================================================================
-- 12. SEED MÍNIMO — necesario para que la app arranque
-- ============================================================================

INSERT OR IGNORE INTO roles (nombre, descripcion) VALUES ('SUPERADMIN', 'Dueño del gimnasio — acceso total al sistema');

-- El listado real de acciones vive en código (authorization.rs) y se inserta
-- en el primer arranque — no se hardcodea aquí para que agregar una acción
-- nueva sea un cambio de código versionado, no una migración manual de datos.

INSERT INTO sedes (nombre, es_activa)
SELECT 'Sede Principal', 1
WHERE NOT EXISTS (SELECT 1 FROM sedes WHERE nombre = 'Sede Principal' AND deleted_at IS NULL);

-- ============================================================================
-- FIN
-- ============================================================================