namespace SmartGym.Core.Services;

/// <summary>
/// Configuración local del equipo (no sincronizada — mismo criterio que
/// perifericos_config del proyecto Tauri original: datos propios de esta
/// terminal física, nunca de la cuenta ni de la sede en el servidor). Por
/// ahora solo guarda a qué sede pertenece el equipo, para el Kiosco.
/// </summary>
public interface ITerminalConfigStore
{
    Task<long?> ObtenerIdSedeAsync();
    Task GuardarIdSedeAsync(long idSede);
}
