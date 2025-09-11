using System.ComponentModel;

namespace SysBot.Pokemon;

/// <summary>
/// Settings for the Web Control Panel server
/// </summary>
public sealed class WebServerSettings
{
    private const string WebServer = nameof(WebServer);

    [Category(WebServer)]
    [Description("El número de puerto para la interfaz web del Panel de Control del Bot. Por defecto es 8080.")]
    public int ControlPanelPort { get; set; } = 8080;

    [Category(WebServer)]
    [Description("Habilitar o deshabilitar el panel de control web. Cuando está deshabilitado, la interfaz web no será accesible.")]
    public bool EnableWebServer { get; set; } = true;

    [Category(WebServer)]
    [Description("Permitir conexiones externas al panel de control web. Cuando es falso, solo se permiten conexiones desde localhost.")]
    public bool AllowExternalConnections { get; set; } = false;
}
