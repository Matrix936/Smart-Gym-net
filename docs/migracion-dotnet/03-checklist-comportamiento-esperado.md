# 03 — Checklist de Comportamiento Esperado (122 tests Rust → tests .NET)

**Fuente:** `src-tauri/src/*.rs` — extraídos los nombres reales de los 122 `#[test]`/`#[tokio::test]`.

**Uso:** cada checkbox es un requisito de comportamiento verificado en la versión Rust. La versión .NET debe tener un test equivalente (xUnit/NUnit) que reproduzca exactamente el mismo escenario. El checklist se va marcando conforme cada test .NET existe y pasa.

**Cómo se extrajo:** script PowerShell sobre los 9 archivos de módulos con tests; el nombre de cada test es el nombre real de la función en Rust.

---

## auth.rs — 13 tests

- [ ] `login_correcto_devuelve_token_y_sesion_valida`
- [ ] `login_password_incorrecta_falla_sin_revelar_email`
- [ ] `login_email_inexistente_da_mismo_error_que_clave_incorrecta` (no enumeración de usuarios)
- [ ] `logout_invalida_sesion_posterior`
- [ ] `requiere_permiso_acepta_y_deniega_correctamente`
- [ ] `validar_sesion_puebla_id_sede_para_usuario_con_sede_y_none_para_superadmin_sin_sede`
- [ ] `reautorizar_con_clave_correcta_devuelve_ok`
- [ ] `reautorizar_con_clave_incorrecta_devuelve_unauthorized_claro`
- [ ] `login_exitoso_agrega_cuenta_recordada`
- [ ] `login_fallido_no_toca_cuentas_recordadas`
- [ ] `segundo_login_actualiza_ultimo_login_sin_duplicar`
- [ ] `listar_cuentas_recordadas_devuelve_vacio_en_instalacion_nueva`
- [ ] `login_guarda_en_sesion_actual_state_y_logout_limpia`

## authorization.rs — 5 tests

- [ ] `seed_inserta_todas_acciones_para_superadmin_cuando_tabla_vacia`
- [ ] `seed_es_idempotente_no_duplica_si_ya_poblada`
- [ ] `seed_habilita_cobranza_registrar_abono_para_superadmin`
- [ ] `requiere_permiso_llama_a_una_accion_no_presente_es_denegada`
- [ ] `seed_no_corre_si_permisos_rol_ya_tiene_filas`

## members.rs — 13 tests

- [ ] `create_member_genera_id_uuid_y_get_member_lo_recupera`
- [ ] `create_member_superadmin_sin_sede_usa_id_sede_del_frontend_valida_existencia`
- [ ] `create_member_con_sesion_local_ignora_id_sede_del_frontend`
- [ ] `create_member_sin_sede_ni_frontend_da_error_validacion`
- [ ] `create_member_email_invalido_es_rechazado`
- [ ] `create_member_nombre_vacio_es_rechazado`
- [ ] `search_members_encuentra_por_nombre_email_o_telefono`
- [ ] `update_member_actualiza_campos_seleccionados_y_preserva_id_sede_y_id`
- [ ] `update_member_email_invalido_rechaza`
- [ ] `cambiar_estado_socio_actualiza_estado_y_registra_historial`
- [ ] `cambiar_estado_socio_rechaza_estado_invalido`
- [ ] `soft_delete_marca_deleted_at_y_oculta_de_get_y_search`
- [ ] `cambiar_estado_socio_es_atomico_falla_si_socio_inexistente`

## memberships.rs — 14 tests

- [ ] `vender_membresia_exitosa_crea_membresia_pago_y_movimiento_caja`
- [ ] `vender_membresia_con_monto_menor_genera_cuenta_cobrar_con_saldo`
- [ ] `vender_membresia_sin_caja_abierta_da_error_claro`
- [ ] `vender_membresia_monto_negativo_o_excesivo_es_rechazado`
- [ ] `vender_membresia_plan_inexistente_da_not_found`
- [ ] `vender_membresia_socio_inexistente_da_not_found`
- [ ] `renovacion_reusa_fecha_fin_anterior_no_pierde_dias`
- [ ] `congelar_membresia_respeta_dias_max_y_extiende_fecha_fin`
- [ ] `congelar_membresia_excede_dias_max_da_error`
- [ ] `congelar_membresia_inexistente_da_not_found`
- [ ] `cancelar_membresia_con_clave_correcta_funciona`
- [ ] `cancelar_membresia_con_clave_incorrecta_falla`
- [ ] `cancelar_membresia_inexistente_da_not_found`
- [ ] `cancelar_membresia_ya_cancelada_da_conflict`

## access.rs — 15 tests

- [ ] `kiosko_concedido_membresia_activa`
- [ ] `kiosko_concedido_alternancia_tipo`
- [ ] `kiosko_denegado_membresia_vencida`
- [ ] `kiosko_denegado_membresia_congelada`
- [ ] `kiosko_socio_bloqueado_denegado`
- [ ] `kiosko_socio_inactivo_denegado`
- [ ] `kiosko_socio_inexistente`
- [ ] `kiosko_dispositivo_invalido`
- [ ] `kiosko_dispositivo_none_es_null`
- [ ] `manual_concedido_membresia_activa`
- [ ] `manual_denegado_membresia_vencida`
- [ ] `manual_sin_sesion_falla`
- [ ] `manual_sin_permiso_falla`
- [ ] `fecha_ultimo_acceso_actualizada_solo_concedido`
- [ ] `kiosko_prioriza_membresia_activa_sobre_cancelada_y_vencidas`

## pos.rs — 13 tests

- [ ] `registrar_venta_exitosa_multi_item`
- [ ] `registrar_venta_con_socio_opcional`
- [ ] `registrar_venta_sin_items_da_validation`
- [ ] `registrar_venta_metodo_pago_vacio_da_validation`
- [ ] `registrar_venta_stock_insuficiente_da_conflict`
- [ ] `registrar_venta_producto_inexistente_da_not_found`
- [ ] `registrar_venta_sin_caja_abierta_da_conflict`
- [ ] `cancelar_venta_exitosa_restituye_stock`
- [ ] `cancelar_venta_con_clave_incorrecta_falla`
- [ ] `cancelar_venta_ya_cancelada_da_conflict`
- [ ] `cancelar_venta_inexistente_da_not_found`
- [ ] `cancelar_venta_sin_caja_abierta_da_conflict`
- [ ] `cancelar_venta_calcula_monto_esperado_correctamente`

## cash.rs — 12 tests

- [ ] `abrir_caja_exitosa_devuelve_sesion_abierta`
- [ ] `abrir_caja_monto_inicial_negativo_es_rechazado`
- [ ] `abrir_caja_doble_en_misma_sede_falla_con_conflict_sin_importar_usuario`
- [ ] `abrir_caja_sin_sede_para_sa_sin_param_falla_validacion`
- [ ] `abrir_caja_superadmin_con_param_sede_valida_funciona_si_sede_activa`
- [ ] `cerrar_caja_calcula_monto_esperado_con_movimientos_mixtos`
- [ ] `cerrar_caja_sin_movimientos_da_esperado_igual_a_inicial`
- [ ] `cerrar_caja_inexistente_da_not_found`
- [ ] `cerrar_caja_ya_cerrada_da_conflict`
- [ ] `cerrar_caja_monto_negativo_es_rechazado`
- [ ] `obtener_caja_abierta_devuelve_some_cuando_h_abierta_y_none_cuando_no`
- [ ] `obtener_caja_abierta_no_encuentra_cerradas`

## biometrics.rs — 20 tests

**Parsing de respuestas del sidecar:**
- [ ] `parse_health_response`
- [ ] `parse_enroll_status_completado`
- [ ] `parse_enroll_status_error`
- [ ] `parse_enroll_status_capturando`
- [ ] `parse_identify_identificado`
- [ ] `parse_identify_no_identificado`

**Sync de templates (enrolamiento/re-enrolamiento):**
- [ ] `enrollment_sync_desactiva_template_anterior`
- [ ] `enrollment_sync_dedo_diferente_no_desactiva`
- [ ] `enrollment_sync_falla_si_socio_no_existe_en_tabla_biometricos`

**Serialización de eventos:**
- [ ] `identification_event_serializa_correctamente`
- [ ] `enrollment_event_serializa_correctamente`
- [ ] `enrollment_event_con_error_excluye_template`

**Selección de templates por sede (membresía):**
- [ ] `templates_sede_socio_sin_membresia_devuelve_vacio`
- [ ] `templates_sede_socio_con_membresia_activa_devuelve_template`
- [ ] `templates_sede_socio_con_membresia_congelada_devuelve_template`
- [ ] `templates_sede_socio_con_membresia_vencida_no_devuelve_template`
- [ ] `templates_sede_socio_registrado_en_otra_sede_con_membresia_aqui_si_aparece`
- [ ] `templates_sede_socio_con_membresias_en_dos_sedes_aparece_en_ambas`
- [ ] `templates_sede_sin_huellas_registradas_devuelve_vacio`
- [ ] `templates_sede_distinct_evita_duplicados_por_multiples_membresias`

## setup.rs — 17 tests

- [ ] `verificar_estado_tabla_vacia_retorna_configuracion_pendiente`
- [ ] `verificar_estado_con_usuario_retorna_configuracion_completa`
- [ ] `completar_configuracion_inicial_exitoso`
- [ ] `completar_configuracion_inicial_sin_datos_fiscales_exitoso`
- [ ] `completar_configuracion_password_corta_rechaza`
- [ ] `completar_configuracion_email_invalido_rechaza`
- [ ] `completar_configuracion_nombre_comercial_vacio_rechaza`
- [ ] `completar_configuracion_rechaza_si_ya_existe_usuario`
- [ ] `obtener_datos_empresa_con_empresa_configurada`
- [ ] `obtener_datos_empresa_sin_empresa_retorna_not_found`
- [ ] `obtener_datos_empresa_con_logo`
- [ ] `guardar_logo_mime_no_permitido_rechaza`
- [ ] `guardar_logo_tamanio_excesivo_rechaza`
- [ ] `guardar_logo_valido_png_guarda_archivo_deterministico`
- [ ] `guardar_logo_cambia_formato_elimina_huerfanos`
- [ ] `guardar_logo_mismo_formato_no_elimina`
- [ ] `completar_configuracion_con_logo_guarda_path_en_db`

---

## Cómo usar la suite actual para la verificación

- Comando actual: `cargo test` dentro de `src-tauri/` (corre los tests de los 9 módulos).
- `docs/auditorias/conteo_por_modulo.txt` es una corrida más antigua (65 tests en 8 módulos); la lista de esta carpeta es el estado actual completo (122) extraído del código fuente.
- Al portar un módulo a .NET, traducir cada test 1:1 (mismo nombre en español, mismo escenario, mismas aserciones de error: `not_found`, `conflict`, `validation`, `unauthorized`).

## Invariantes estructurales (tests adicionales que ya exige el diseño)

Del documento de decisiones (§2.14) y `04-seguridad` (§8), la suite Rust mantiene invariantes que el port a .NET debe replicar como tests:

- [ ] Todo comando de escritura valida sesión y permiso.
- [ ] Ninguna columna monetaria es `REAL`/`FLOAT`.
- [ ] Las 4 tablas local-only nunca aparecen en el worker de sync.
- [ ] Errores de bajo nivel no se serializan al frontend (solo variantes de negocio).
- [ ] `PRAGMA foreign_keys = ON` en cada conexión.
- [ ] Lote de sync conservador (10–15) con reintento adaptativo.
