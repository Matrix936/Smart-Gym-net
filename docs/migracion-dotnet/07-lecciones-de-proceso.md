# 07 — Lecciones de Proceso (verificación con hardware real)

**Propósito:** no es un documento técnico. Es una regla de **proceso de desarrollo** para el equipo que reimplementa en .NET: aquí se capturan los ciclos de "funciona" → "no funciona en realidad" que ya nos costaron tiempo en el proyecto actual, para no repetirlos.

**Origen:** sesión de investigación del sidecar biométrico (julio 2026, ver `docs/investigacion-sidecar-biometrico.md`). Los ejemplos citados vienen de esa sesión; la lección aplica a cualquier integración con hardware (lector de huella, impresora, código de barras).

---

## La lección central

> **"Funciona" no es un resultado de verificación. Lo es una reproducción controlada, con variables aisladas, hardware real, build limpio y un registro de qué se probó exactamente.**

En la sesión del sidecar pasamos por ciclos donde una configuración "funcionaba" (Tests 1–7) y luego, con una variable ambiental distinta (`WbioSrvc` detenido), **dejó de funcionar sin que el código hubiera cambiado**. Peor aún: en un momento se concluyó "arreglado" con un binario que no era el código que creíamos probar (stale build). Ambos casos son errores de verificación, no de hardware.

---

## Reglas de proceso para el equipo .NET

### R1. Verificar con hardware real en **cada** ciclo, no solo al final
No abandonar la máquina física (lector U.are.U 4500, impresora) a "pruebas que yo ya hice". Cada cambio significativo al sidecar, al Kiosco o a la impresión se prueba contra el hardware real. El código puede pasar todos los unit tests y fallar contra el hardware por razones que los tests no cubren (p. ej., message pump COM, drivers, servicios).

### R2. Aislar **una** variable a la vez
Cuando algo deje de funcionar, no cambiar tres cosas a la vez (estado de servicio + ventana + driver + build) y esperar que "alguna arregle". La metodología de los 10 tests de la investigación (una variable por test, tabla de resultados) fue lo que permitió encontrar la causa raíz. Reproducirla: hipótesis → cambio de una variable → verificación → registrarlo.

### R3. Build limpio antes de concluir
Antes de declarar "arreglado", hacer `clean` + build y verificar el timestamp del binario / que el log cargado sea del código actual. El binario stale engañó una conclusión en la sesión (el código decía una cosa, el `.exe` probado era otro). Regla concreta: **nunca verificar un binario que no se acaba de compilar**.

### R4. Registrar el entorno exacto de cada prueba
El resultado de una prueba sin su entorno no sirve: estado de `WbioSrvc`, drivers habilitados/deshabilitados, versión de DLLs, timestamps, reconnects USB. En el proyecto, el Test 6 "funcionaba" — pero solo con `WbioSrvc` corriendo; esa variable no estaba aislada y por eso se concluyó mal. **Cada fila de resultado debe traer su configuración.**

### R5. Verificar con conteos observables, no con impresiones
No concluir por "se vio que pasó". En la sesión, el veredicto final se sostuvo con números: 8 `OnFingerTouch`, 6 `OnComplete`. Usar contadores y logs con timestamp para decidir. Si el criterio no es medible, todavía no es un criterio.

### R6. Cuando "funciona" y "no funciona" alternan sin cambio de código → sospechar entorno
El síntoma intermitente casi siempre es una variable ambiental no controlada (servicio Windows, driver, política corporativa, reloj, otro proceso). Registrar cuándo funciona/no funciona y buscar el patrón (¿qué estaba distinto?) antes de tocar código.

### R7. Desconfiar del "hardware como culpable" por defecto
En la sesión, el hardware estaba bien; el problema era una dependencia no documentada del SDK. Antes de reemplazar/reinstalar hardware, agotar las variables ambientales y del proceso (servicios, hilo UI, ventana).

---

## Checklist corto antes de marcar cualquier integración de hardware como "hecha"

- [ ] Probado con el hardware real en la configuración de producción (mismo driver, mismo servicio en el estado realista).
- [ ] Al menos una prueba desde arranque en frío (la app arranca y el primer uso del hardware funciona).
- [ ] Al menos una prueba con la condición adversa relevante (p. ej., `WbioSrvc` detenido) si existe una.
- [ ] Build limpio y binario verificado como el probado.
- [ ] Resultado respaldado por conteos/logs con timestamp, no por impresión subjetiva.
- [ ] Entorno de la prueba documentado en el repo (mismo archivo que el resultado).

---

## Relación con el resto del paquete

- La lección técnica concreta (ventana visible, `Activate()` marshalado, `WbioSrvc`, auto-arranque bajo demanda, contención de hilos) vive en `04-integracion-biometrica.md`.
- La regla de "no asumas que es código" y el checklist operativo del sensor están en `04 §9`.
- Este documento es la versión **de proceso**: aplica a biometría, impresión y cualquier integración con hardware del proyecto .NET.