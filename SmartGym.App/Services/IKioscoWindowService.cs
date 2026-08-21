namespace SmartGym.App.Services;

/// <summary>
/// Abre/cierra la ventana del Kiosco (BlazorWebView propio, ver KioscoPage.razor).
/// Singleton: solo puede haber una ventana de Kiosco abierta a la vez; si ya está
/// abierta, Abrir() la trae al frente en vez de crear una segunda.
/// </summary>
public interface IKioscoWindowService
{
    void Abrir();
}
