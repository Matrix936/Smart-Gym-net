using SmartGym.Core.Authorization;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Core.Repositories;

namespace SmartGym.Core.Services;

public sealed class PromocionesService : IPromocionesService
{
    private readonly IAuthService _auth;
    private readonly IAuthorizationService _authz;
    private readonly IPromocionesRepository _promociones;
    private readonly IProductosRepository _productos;
    private readonly IBitacoraAuditoriaRepository _bitacora;

    public PromocionesService(
        IAuthService auth,
        IAuthorizationService authz,
        IPromocionesRepository promociones,
        IProductosRepository productos,
        IBitacoraAuditoriaRepository bitacora)
    {
        _auth = auth;
        _authz = authz;
        _promociones = promociones;
        _productos = productos;
        _bitacora = bitacora;
    }

    /// <summary>
    /// Vigencia efectiva — mismo criterio que MembresiaEstadoCalculator: estado
    /// calculado al momento, la columna cruda nunca se muta por paso del tiempo.
    /// Fechas date-only 'yyyy-MM-dd' → comparación lexicográfica segura.
    /// </summary>
    public static bool EsVigente(Promocion p, string hoy)
    {
        if (!p.EsActivo || p.DeletedAt is not null)
        {
            return false;
        }
        return (p.FechaInicio is null || string.CompareOrdinal(p.FechaInicio, hoy) <= 0)
            && (p.FechaFin is null || string.CompareOrdinal(p.FechaFin, hoy) >= 0);
    }

    public async Task<PagedResult<PromocionInfo>> BuscarAsync(
        string token, string? query = null, string? tipo = null, bool? esActivo = null,
        int pagina = 1, int tamanoPagina = TamanosPagina.Default, CancellationToken ct = default)
    {
        await _auth.ValidarSesionAsync(token, ct);
        var resultado = await _promociones.SearchAsync(query, tipo, esActivo, pagina, tamanoPagina, ct);
        return new PagedResult<PromocionInfo>
        {
            Items = await ProyectarAsync(resultado.Items, ct),
            TotalRegistros = resultado.TotalRegistros,
            Pagina = resultado.Pagina,
            TamanoPagina = resultado.TamanoPagina,
        };
    }

    public async Task<PromocionInfo> CrearDescuentoAsync(
        string token, string nombre, string? descripcion, long idProducto,
        string tipoDescuento, long valor, DateTime? fechaInicio = null, DateTime? fechaFin = null,
        CancellationToken ct = default)
    {
        var (info, idPromocion) = await CrearCoreAsync(token, nombre, descripcion, fechaInicio, fechaFin, ct);

        var producto = await ValidarProductoAsync(idProducto, ct);
        ValidarTipoYValorDescuento(tipoDescuento, valor);
        await RechazarSolapadoAsync(idProducto, fechaInicio, fechaFin, excluirId: null, ct);

        var promo = new Promocion
        {
            IdPromocion = idPromocion,
            Tipo = PromocionTipos.Descuento,
            Nombre = nombre.Trim(),
            Descripcion = Limpiar(descripcion),
            IdProducto = idProducto,
            TipoDescuento = tipoDescuento,
            Valor = valor,
            FechaInicio = Normalizar(fechaInicio),
            FechaFin = Normalizar(fechaFin),
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        };

        await _promociones.InsertAsync(promo, Array.Empty<PromocionComponente>(), ct);
        await RegistrarBitacoraAsync(info, "promocion.creada", promo, $"producto:{producto.Descripcion}", ct);
        return await ObtenerInfoAsync(promo.IdPromocion, ct);
    }

    public async Task<PromocionInfo> CrearComboAsync(
        string token, string nombre, string? descripcion, long precioComboCentavos,
        IReadOnlyList<PromocionComponente> componentes, DateTime? fechaInicio = null, DateTime? fechaFin = null,
        CancellationToken ct = default)
    {
        var (info, idPromocion) = await CrearCoreAsync(token, nombre, descripcion, fechaInicio, fechaFin, ct);

        ValidarPrecioCombo(precioComboCentavos);
        var componentesValidados = await ValidarComponentesAsync(componentes, ct);

        var promo = new Promocion
        {
            IdPromocion = idPromocion,
            Tipo = PromocionTipos.Combo,
            Nombre = nombre.Trim(),
            Descripcion = Limpiar(descripcion),
            PrecioComboCentavos = precioComboCentavos,
            FechaInicio = Normalizar(fechaInicio),
            FechaFin = Normalizar(fechaFin),
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        };

        await _promociones.InsertAsync(promo, componentesValidados, ct);
        await RegistrarBitacoraAsync(info, "promocion.creada", promo,
            $"combo:{componentesValidados.Count} componentes|precio:{precioComboCentavos}", ct);
        return await ObtenerInfoAsync(promo.IdPromocion, ct);
    }

    public async Task<PromocionInfo> EditarDescuentoAsync(
        string token, string idPromocion, string nombre, string? descripcion, long idProducto,
        string tipoDescuento, long valor, DateTime? fechaInicio = null, DateTime? fechaFin = null,
        CancellationToken ct = default)
    {
        var info = await EditarGateAsync(token, idPromocion, ct);
        var existente = await ObtenerTipoAsync(idPromocion, PromocionTipos.Descuento, ct);

        ValidarNombre(nombre);
        var producto = await ValidarProductoAsync(idProducto, ct);
        ValidarTipoYValorDescuento(tipoDescuento, valor);
        ValidarRangoFechas(fechaInicio, fechaFin);
        await RechazarSolapadoAsync(idProducto, fechaInicio, fechaFin, excluirId: idPromocion, ct);

        existente.Nombre = nombre.Trim();
        existente.Descripcion = Limpiar(descripcion);
        existente.IdProducto = idProducto;
        existente.TipoDescuento = tipoDescuento;
        existente.Valor = valor;
        existente.FechaInicio = Normalizar(fechaInicio);
        existente.FechaFin = Normalizar(fechaFin);
        existente.UpdatedAt = DateHelper.NowIsoUtc();

        await _promociones.UpdateAsync(existente, Array.Empty<PromocionComponente>(), ct);
        await RegistrarBitacoraAsync(info, "promocion.editada", existente, $"producto:{producto.Descripcion}", ct);
        return await ObtenerInfoAsync(idPromocion, ct);
    }

    public async Task<PromocionInfo> EditarComboAsync(
        string token, string idPromocion, string nombre, string? descripcion, long precioComboCentavos,
        IReadOnlyList<PromocionComponente> componentes, DateTime? fechaInicio = null, DateTime? fechaFin = null,
        CancellationToken ct = default)
    {
        var info = await EditarGateAsync(token, idPromocion, ct);
        var existente = await ObtenerTipoAsync(idPromocion, PromocionTipos.Combo, ct);

        ValidarNombre(nombre);
        ValidarPrecioCombo(precioComboCentavos);
        ValidarRangoFechas(fechaInicio, fechaFin);
        var componentesValidados = await ValidarComponentesAsync(componentes, ct);

        existente.Nombre = nombre.Trim();
        existente.Descripcion = Limpiar(descripcion);
        existente.PrecioComboCentavos = precioComboCentavos;
        existente.FechaInicio = Normalizar(fechaInicio);
        existente.FechaFin = Normalizar(fechaFin);
        existente.UpdatedAt = DateHelper.NowIsoUtc();

        await _promociones.UpdateAsync(existente, componentesValidados, ct);
        await RegistrarBitacoraAsync(info, "promocion.editada", existente,
            $"combo:{componentesValidados.Count} componentes|precio:{precioComboCentavos}", ct);
        return await ObtenerInfoAsync(idPromocion, ct);
    }

    public async Task ActivarAsync(string token, string idPromocion, CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.PromocionesGestionar, ct);

        await _promociones.SetActivoAsync(idPromocion, true, DateHelper.NowIsoUtc(), ct);
        await _bitacora.InsertAsync(Bitacora(info, "promocion.activada", idPromocion, "inactivo", "activo"), ct);
    }

    public async Task DesactivarAsync(string token, string idPromocion, CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.PromocionesGestionar, ct);

        await _promociones.SetActivoAsync(idPromocion, false, DateHelper.NowIsoUtc(), ct);
        await _bitacora.InsertAsync(Bitacora(info, "promocion.desactivada", idPromocion, "activo", "inactivo"), ct);
    }

    public async Task<IReadOnlyList<PosPromocionInfo>> ObtenerParaPosAsync(string token, CancellationToken ct = default)
    {
        await _auth.ValidarSesionAsync(token, ct);
        var hoy = DateHelper.TodayIso();
        var resultado = await _promociones.SearchAsync(null, null, esActivo: true, 1, TamanosPagina.Cincuenta, ct);
        var vigentes = resultado.Items.Where(p => EsVigente(p, hoy)).ToList();

        var lista = new List<PosPromocionInfo>();
        foreach (var promo in vigentes)
        {
            if (promo.Tipo == PromocionTipos.Descuento)
            {
                var producto = await _productos.GetByIdAsync(promo.IdProducto!.Value, ct);
                if (producto is null || !producto.EsActivo)
                {
                    continue;
                }

                lista.Add(new PosPromocionInfo
                {
                    IdPromocion = promo.IdPromocion,
                    Tipo = promo.Tipo,
                    Nombre = promo.Nombre,
                    IdProducto = promo.IdProducto,
                    PrecioOriginalCentavos = producto.PrecioVentaCentavos,
                    PrecioFinalCentavos = CalcularPrecioFinal(producto.PrecioVentaCentavos, promo),
                });
            }
            else
            {
                var componentesRaw = await _promociones.GetComponentesAsync(promo.IdPromocion, ct);
                var componentes = new List<ComponenteInfo>();
                long subtotal = 0;
                foreach (var c in componentesRaw)
                {
                    var producto = await _productos.GetByIdAsync(c.IdProducto, ct);
                    if (producto is null || !producto.EsActivo)
                    {
                        // Componente inexistente o inactivo → el combo no se ofrece.
                        componentes.Clear();
                        break;
                    }
                    componentes.Add(new ComponenteInfo
                    {
                        IdProducto = c.IdProducto,
                        Cantidad = c.Cantidad,
                        DescripcionProducto = producto.Descripcion,
                        PrecioVentaCentavos = producto.PrecioVentaCentavos,
                    });
                    subtotal += producto.PrecioVentaCentavos * c.Cantidad;
                }

                if (componentes.Count > 0)
                {
                    lista.Add(new PosPromocionInfo
                    {
                        IdPromocion = promo.IdPromocion,
                        Tipo = promo.Tipo,
                        Nombre = promo.Nombre,
                        PrecioComboCentavos = promo.PrecioComboCentavos ?? 0,
                        SubtotalComponentesCentavos = subtotal,
                        Componentes = componentes,
                    });
                }
            }
        }
        return lista;
    }

    /// <summary>Precio final de un producto con descuento aplicado; nunca negativo.</summary>
    public static long CalcularPrecioFinal(long precioVentaCentavos, Promocion descuento)
    {
        if (descuento.TipoDescuento == PromocionTiposDescuento.MontoFijo)
        {
            return Math.Max(0, precioVentaCentavos - (descuento.Valor ?? 0));
        }
        // Porcentaje con redondeo hacia abajo, a favor del gym.
        var rebaja = precioVentaCentavos * (descuento.Valor ?? 0) / 100;
        return Math.Max(0, precioVentaCentavos - rebaja);
    }

    // ------------------------------------------------------------------ core

    private async Task<(SessionInfo info, string idPromocion)> CrearCoreAsync(
        string token, string nombre, string? descripcion, DateTime? fechaInicio, DateTime? fechaFin, CancellationToken ct)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.PromocionesGestionar, ct);
        ValidarNombre(nombre);
        ValidarRangoFechas(fechaInicio, fechaFin);
        return (info, UuidHelper.NewV4());
    }

    private async Task<SessionInfo> EditarGateAsync(string token, string idPromocion, CancellationToken ct)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.PromocionesGestionar, ct);
        return info;
    }

    private async Task<Promocion> ObtenerTipoAsync(string idPromocion, string tipoEsperado, CancellationToken ct)
    {
        var promo = await _promociones.GetByIdAsync(idPromocion, ct)
            ?? throw BusinessException.NotFound("Promoción no encontrada", "promocion_no_encontrada");
        if (promo.Tipo != tipoEsperado)
        {
            throw BusinessException.Validation(
                $"La promoción {idPromocion} no es del tipo {tipoEsperado}", "tipo_promocion_invalido");
        }
        return promo;
    }

    private async Task<Producto> ValidarProductoAsync(long idProducto, CancellationToken ct)
    {
        var producto = await _productos.GetByIdAsync(idProducto, ct)
            ?? throw BusinessException.NotFound("Producto no encontrado o inactivo", "producto_no_encontrado");
        return producto;
    }

    private static void ValidarTipoYValorDescuento(string tipoDescuento, long valor)
    {
        if (tipoDescuento != PromocionTiposDescuento.MontoFijo && tipoDescuento != PromocionTiposDescuento.Porcentaje)
        {
            throw BusinessException.Validation("El tipo de descuento debe ser monto_fijo o porcentaje", "tipo_descuento_invalido");
        }
        if (valor <= 0)
        {
            throw BusinessException.Validation("El valor del descuento debe ser mayor a cero", "valor_invalido");
        }
        if (tipoDescuento == PromocionTiposDescuento.Porcentaje && valor > 100)
        {
            throw BusinessException.Validation("El porcentaje no puede ser mayor a 100", "valor_invalido");
        }
    }

    private static void ValidarPrecioCombo(long precioComboCentavos)
    {
        if (precioComboCentavos <= 0)
        {
            throw BusinessException.Validation("El precio del combo debe ser mayor a cero", "precio_combo_invalido");
        }
    }

    private async Task<IReadOnlyList<PromocionComponente>> ValidarComponentesAsync(IReadOnlyList<PromocionComponente>? componentes, CancellationToken ct)
    {
        if (componentes is null || componentes.Count == 0)
        {
            throw BusinessException.Validation("Un combo requiere al menos un componente", "combo_sin_componentes");
        }

        var validados = new List<PromocionComponente>();
        foreach (var c in componentes)
        {
            if (c.Cantidad <= 0)
            {
                throw BusinessException.Validation(
                    $"La cantidad del producto {c.IdProducto} debe ser mayor a cero", "componente_cantidad_invalida");
            }
            _ = await ValidarProductoAsync(c.IdProducto, ct);
            validados.Add(new PromocionComponente { IdProducto = c.IdProducto, Cantidad = c.Cantidad });
        }
        return validados;
    }

    /// <summary>
    /// Regla acordada: NO se resuelven solapamientos automáticamente — si ya
    /// existe un descuento activo sobre el producto con rango que cruza el nuevo,
    /// el alta se rechaza con error explícito.
    /// </summary>
    private async Task RechazarSolapadoAsync(long idProducto, DateTime? fechaInicio, DateTime? fechaFin, string? excluirId, CancellationToken ct)
    {
        var solapado = await _promociones.GetDescuentoSolapadoAsync(
            idProducto, Normalizar(fechaInicio), Normalizar(fechaFin), excluirId, ct);
        if (solapado is not null)
        {
            throw BusinessException.Conflict(
                $"El producto ya tiene un descuento activo que se cruza con estas fechas: \"{solapado.Nombre}\"",
                "descuento_solapado");
        }
    }

    private static void ValidarNombre(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            throw BusinessException.Validation("El nombre es obligatorio", "nombre_obligatorio");
        }
    }

    private static void ValidarRangoFechas(DateTime? fechaInicio, DateTime? fechaFin)
    {
        if (fechaInicio.HasValue && fechaFin.HasValue && fechaFin.Value.Date < fechaInicio.Value.Date)
        {
            throw BusinessException.Validation("La fecha fin no puede ser anterior a la fecha inicio", "fecha_rango_invalido");
        }
    }

    private async Task<PromocionInfo> ObtenerInfoAsync(string idPromocion, CancellationToken ct)
    {
        var promo = await _promociones.GetByIdAsync(idPromocion, ct)
            ?? throw BusinessException.NotFound("Promoción no encontrada", "promocion_no_encontrada");
        var infos = await ProyectarAsync([promo], ct);
        return infos[0];
    }

    /// <summary>Proyecta promociones a PromocionInfo resolviendo producto y componentes.</summary>
    private async Task<List<PromocionInfo>> ProyectarAsync(IEnumerable<Promocion> promos, CancellationToken ct)
    {
        var lista = new List<PromocionInfo>();
        foreach (var promo in promos)
        {
            var info = new PromocionInfo { Promocion = promo };
            if (promo.IdProducto is not null)
            {
                info.DescripcionProducto = (await _productos.GetByIdAsync(promo.IdProducto.Value, ct))?.Descripcion;
            }
            else
            {
                var componentesRaw = await _promociones.GetComponentesAsync(promo.IdPromocion, ct);
                var componentes = new List<ComponenteInfo>();
                foreach (var c in componentesRaw)
                {
                    var producto = await _productos.GetByIdAsync(c.IdProducto, ct);
                    if (producto is null) continue;
                    componentes.Add(new ComponenteInfo
                    {
                        IdProducto = c.IdProducto,
                        Cantidad = c.Cantidad,
                        DescripcionProducto = producto.Descripcion,
                        PrecioVentaCentavos = producto.PrecioVentaCentavos,
                    });
                    info.SubtotalComponentesCentavos += producto.PrecioVentaCentavos * c.Cantidad;
                }
                info.Componentes = componentes;
            }
            lista.Add(info);
        }
        return lista;
    }

    private async Task RegistrarBitacoraAsync(
        SessionInfo info, string accion, Promocion promo, string detalleNuevo, CancellationToken ct)
    {
        var anterior = accion == "promocion.editada"
            ? $"{promo.Tipo}:{promo.Nombre}"
            : null;
        await _bitacora.InsertAsync(Bitacora(info, accion, promo.IdPromocion, anterior, detalleNuevo), ct);
    }

    private static BitacoraAuditoria Bitacora(
        SessionInfo info, string accion, string idRegistro, string? anterior, string? nuevo) =>
        new()
        {
            IdRegistro = UuidHelper.NewV4(),
            IdUsuario = info.IdUsuario,
            Accion = accion,
            TablaAfectada = "promociones",
            IdRegistroAfectado = idRegistro,
            ValorAnterior = anterior,
            ValorNuevo = nuevo,
            IdSede = info.IdSede,
            CreatedAt = DateHelper.NowIsoUtc(),
            UpdatedAt = DateHelper.NowIsoUtc(),
        };

    private static string? Limpiar(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    private static string? Normalizar(DateTime? fecha) =>
        fecha.HasValue ? DateHelper.ToFechaSolo(fecha.Value) : null;
}
