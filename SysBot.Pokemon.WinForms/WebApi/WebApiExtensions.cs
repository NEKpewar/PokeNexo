using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SysBot.Base;
using System.Diagnostics;
using SysBot.Pokemon.Helpers;
using SysBot.Pokemon.WinForms.WebApi;
using static SysBot.Pokemon.WinForms.WebApi.RestartManager;
using System.Collections.Concurrent;

namespace SysBot.Pokemon.WinForms;

public static class WebApiExtensions
{
    private static BotServer? _server;
    private static TcpListener? _tcp;
    private static CancellationTokenSource? _cts;
    private static CancellationTokenSource? _monitorCts;
    private static Main? _main;

    private static int _webPort = 8080; // Will be set from config
    private static int _tcpPort = 0;
    private static readonly object _portLock = new object();
    private static readonly ConcurrentDictionary<int, DateTime> _portReservations = new();

    public static void InitWebServer(this Main mainForm)
    {
        _main = mainForm;

        // Get the configured port from settings
        if (mainForm.Config?.Hub?.WebServer != null)
        {
            _webPort = mainForm.Config.Hub.WebServer.ControlPanelPort;

            // Validate port range
            if (_webPort < 1 || _webPort > 65535)
            {
                LogUtil.LogError($"Puerto del servidor web inválido {_webPort}. Usando el puerto predeterminado 8080.", "WebServer");
                _webPort = 8080;
            }

            // Update the UpdateManager with the configured port
            UpdateManager.SetConfiguredWebPort(_webPort);

            // Check if web server is enabled
            if (!mainForm.Config.Hub.WebServer.EnableWebServer)
            {
                LogUtil.LogInfo("El Panel de Control Web está deshabilitado en la configuración.", "WebServer");
                return;
            }

            LogUtil.LogInfo($"El Panel de Control Web se alojará en el puerto {_webPort}", "WebServer");
        }
        else
        {
            // No config available, use default and update UpdateManager
            UpdateManager.SetConfiguredWebPort(_webPort);
        }

        try
        {
            CleanupStalePortFiles();

            CheckPostRestartStartup(mainForm);

            if (IsPortInUse(_webPort))
            {
                lock (_portLock)
                {
                    _tcpPort = FindAvailablePort(8081);
                    ReservePort(_tcpPort);
                }
                StartTcpOnly();

                StartMasterMonitor();
                RestartManager.Initialize(mainForm, _tcpPort);
                // Check for any pending update state and attempt to resume
                _ = Task.Run(async () =>
                {
                    await Task.Delay(10000); // Wait for system to stabilize
                    var currentState = UpdateManager.GetCurrentState();
                    if (currentState != null && !currentState.IsComplete)
                    {
                        LogUtil.LogInfo($"Se encontró una sesión de actualización incompleta {currentState.SessionId}, intentando reanudar", "WebServer");
                        await UpdateManager.StartOrResumeUpdateAsync(mainForm, _tcpPort);
                    }
                });
                
                return;
            }

            TryAddUrlReservation(_webPort);

            lock (_portLock)
            {
                _tcpPort = FindAvailablePort(8081);
                ReservePort(_tcpPort);
            }
            StartFullServer();

            RestartManager.Initialize(mainForm, _tcpPort);
            // Check for any pending update state and attempt to resume
            _ = Task.Run(async () =>
            {
                await Task.Delay(10000); // Wait for system to stabilize
                var currentState = UpdateManager.GetCurrentState();
                if (currentState != null && !currentState.IsComplete)
                {
                    LogUtil.LogInfo($"Se encontró una sesión de actualización incompleta {currentState.SessionId}, intentando reanudar", "WebServer");
                    await UpdateManager.StartOrResumeUpdateAsync(mainForm, _tcpPort);
                }
            });
            
            // Periodically clean up completed update sessions
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromMinutes(30));
                    UpdateManager.ClearState();
                }
            });
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"No se pudo inicializar el servidor web: {ex.Message}", "WebServer");
        }
    }

    private static void ReservePort(int port)
    {
        _portReservations[port] = DateTime.Now;
    }

    private static void ReleasePort(int port)
    {
        _portReservations.TryRemove(port, out _);
    }

    private static void CleanupStalePortFiles()
    {
        try
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
            var exeDir = Path.GetDirectoryName(exePath) ?? Program.WorkingDirectory;

            // Also clean up stale port reservations (older than 5 minutes)
            var now = DateTime.Now;
            var staleReservations = _portReservations
                .Where(kvp => (now - kvp.Value).TotalMinutes > 5)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var port in staleReservations)
            {
                _portReservations.TryRemove(port, out _);
            }

            var portFiles = Directory.GetFiles(exeDir, "PokeNexo_*.port");

            foreach (var portFile in portFiles)
            {
                try
                {
                    var fileName = Path.GetFileNameWithoutExtension(portFile);
                    var pidStr = fileName.Substring("PokeNexo_".Length);

                    if (int.TryParse(pidStr, out int pid))
                    {
                        if (pid == Environment.ProcessId)
                            continue;

                        try
                        {
                            var process = Process.GetProcessById(pid);
                            if (process.ProcessName.Contains("SysBot", StringComparison.OrdinalIgnoreCase) ||
                                process.ProcessName.Contains("PokeNexo", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                        }
                        catch (ArgumentException)
                        {
                        }

                        File.Delete(portFile);
                        LogUtil.LogInfo($"Archivo de puerto obsoleto eliminado: {Path.GetFileName(portFile)}", "WebServer");
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogError($"Error procesando el archivo de puerto {portFile}: {ex.Message}", "WebServer");
                }
            }
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error al limpiar archivos de puerto obsoletos: {ex.Message}", "WebServer");
        }
    }

    private static void StartMasterMonitor()
    {
        _monitorCts = new CancellationTokenSource();

        Task.Run(async () =>
        {
            var random = new Random();

            while (!_monitorCts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(10000 + random.Next(5000), _monitorCts.Token);

                    if (UpdateManager.IsUpdateInProgress() || RestartManager.IsRestartInProgress)
                    {
                        continue;
                    }

                    if (!IsPortInUse(_webPort))
                    {
                        LogUtil.LogInfo("El servidor web maestro está caído. Intentando tomar el control...", "WebServer");

                        await Task.Delay(random.Next(1000, 3000));

                        if (!IsPortInUse(_webPort) && !UpdateManager.IsUpdateInProgress() && !RestartManager.IsRestartInProgress)
                        {
                            TryTakeOverAsMaster();
                            break;
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogUtil.LogError($"Error en el monitor del maestro: {ex.Message}", "WebServer");
                }
            }
        }, _monitorCts.Token);
    }

    private static void TryTakeOverAsMaster()
    {
        try
        {
            TryAddUrlReservation(_webPort);

            _server = new BotServer(_main!, _webPort, _tcpPort);
            _server.Start();

            _monitorCts?.Cancel();
            _monitorCts = null;

            LogUtil.LogInfo($"Se tomó el control con éxito como servidor web maestro en el puerto {_webPort}", "WebServer");
            LogUtil.LogInfo($"La interfaz web ahora está disponible en http://localhost:{_webPort}", "WebServer");
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"No se pudo tomar el control como maestro: {ex.Message}", "WebServer");
            StartMasterMonitor();
        }
    }

    private static bool TryAddUrlReservation(int port)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"http add urlacl url=http://+:{port}/ user=Everyone",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Verb = "runas"
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void StartTcpOnly()
    {
        StartTcp();
        
        // Slaves no longer need their own web server - logs are read directly from file by master
        
        CreatePortFile();
    }

    private static void StartFullServer()
    {
        try
        {
            _server = new BotServer(_main!, _webPort, _tcpPort);
            _server.Start();
            StartTcp();
            CreatePortFile();
        }
        catch (Exception ex) when (ex.Message.Contains("conflicts with an existing registration"))
        {
            // Otra instancia se convirtió en maestro primero - pasar a esclavo de manera segura
            LogUtil.LogInfo($"Conflicto de puerto {_webPort} durante el inicio, iniciando como esclavo", "WebServer");
            StartTcpOnly();  // Esto creará el archivo de puerto como esclavo
        }
    }

    private static void StartTcp()
    {
        _cts = new CancellationTokenSource();
        Task.Run(() => StartTcpListenerAsync(_cts.Token));
    }
    
    private static async Task StartTcpListenerAsync(CancellationToken cancellationToken)
    {
        const int maxRetries = 5;
        var random = new Random();
        
        for (int retry = 0; retry < maxRetries && !cancellationToken.IsCancellationRequested; retry++)
        {
            try
            {
                _tcp = new TcpListener(System.Net.IPAddress.Loopback, _tcpPort);
                _tcp.Start();

                LogUtil.LogInfo($"Escucha TCP iniciada correctamente en el puerto {_tcpPort}", "TCP");

                await AcceptClientsAsync(cancellationToken);
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse && retry < maxRetries - 1)
            {
                LogUtil.LogInfo($"Puerto TCP {_tcpPort} en uso, buscando un nuevo puerto (intento {retry + 1}/{maxRetries})", "TCP");
                await Task.Delay(random.Next(500, 1500), cancellationToken);
                
                lock (_portLock)
                {
                    ReleasePort(_tcpPort);
                    _tcpPort = FindAvailablePort(_tcpPort + 1);
                    ReservePort(_tcpPort);
                }
                
                CreatePortFile();
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                LogUtil.LogError($"Error del listener TCP: {ex.Message}", "TCP");

                if (retry == maxRetries - 1)
                {
                    LogUtil.LogError($"No se pudo iniciar el listener TCP después de {maxRetries} intentos", "TCP");
                    throw new InvalidOperationException("No se pudo encontrar un puerto TCP disponible");
                }
            }
        }
    }
    
    private static async Task AcceptClientsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var tcpTask = _tcp!.AcceptTcpClientAsync();
                var tcs = new TaskCompletionSource<bool>();
                
                using var registration = cancellationToken.Register(() => tcs.SetCanceled());
                var completedTask = await Task.WhenAny(tcpTask, tcs.Task);
                
                if (completedTask == tcpTask && tcpTask.IsCompletedSuccessfully)
                {
                    _ = HandleClientSafelyAsync(tcpTask.Result);
                }
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
    
    private static async Task HandleClientSafelyAsync(TcpClient client)
    {
        try
        {
            await HandleClient(client);
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error no controlado en HandleClient: {ex.Message}", "TCP");
        }
    }

    private static async Task HandleClient(TcpClient client)
    {
        try
        {
            using (client)
            {
                client.ReceiveTimeout = 5000;
                client.SendTimeout = 5000;

                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                var command = await reader.ReadLineAsync();
                if (!string.IsNullOrEmpty(command))
                {
                    var response = await ProcessCommandAsync(command);
                    await writer.WriteLineAsync(response);
                    await writer.FlushAsync();
                }
            }
        }
        catch (IOException ex) when (ex.InnerException is SocketException)
        {
            // Normal disconnection - don't log as error
        }
        catch (ObjectDisposedException)
        {
            // Normal during shutdown
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error al manejar el cliente TCP: {ex.Message}", "TCP");
        }
    }
    
    private static async Task<string> ProcessCommandAsync(string command)
    {
        return await Task.Run(() => ProcessCommand(command));
    }

    private static string ProcessCommand(string command)
    {
        if (_main == null)
            return "ERROR: El formulario principal no está inicializado";

        var parts = command.Split(':');
        var cmd = parts[0].ToUpperInvariant();
        var botId = parts.Length > 1 ? parts[1] : null;

        return cmd switch
        {
            "STARTALL" => ExecuteGlobalCommand(BotControlCommand.Start),
            "STOPALL" => ExecuteGlobalCommand(BotControlCommand.Stop),
            "IDLEALL" => ExecuteGlobalCommand(BotControlCommand.Idle),
            "RESUMEALL" => ExecuteGlobalCommand(BotControlCommand.Resume),
            "RESTARTALL" => ExecuteGlobalCommand(BotControlCommand.Restart),
            "REBOOTALL" => ExecuteGlobalCommand(BotControlCommand.RebootAndStop),
            "SCREENONALL" => ExecuteGlobalCommand(BotControlCommand.ScreenOnAll),
            "SCREENOFFALL" => ExecuteGlobalCommand(BotControlCommand.ScreenOffAll),
            "LISTBOTS" => GetBotsList(),
            "STATUS" => GetBotStatuses(botId),
            "ISREADY" => CheckReady(),
            "INFO" => GetInstanceInfo(),
            "VERSION" => PokeNexo.Version,
            "UPDATE" => TriggerUpdate(),
            "SELFRESTARTALL" => TriggerSelfRestart(),
            "RESTARTSCHEDULE" => GetRestartSchedule(),
            "REMOTE_BUTTON" => HandleRemoteButton(parts),
            "REMOTE_MACRO" => HandleRemoteMacro(parts),
            _ => $"ERROR: Comando desconocido '{cmd}'"
        };
    }

    private static volatile bool _updateInProgress = false;
    private static readonly object _updateLock = new();
    
    private static string TriggerUpdate()
    {
        try
        {
            lock (_updateLock)
            {
                if (_updateInProgress)
                {
                    LogUtil.LogInfo("Actualización ya en progreso - ignorando solicitud duplicada", "WebApiExtensions");
                    return "Actualización ya en progreso";
                }
                _updateInProgress = true;
            }

            if (_main == null)
            {
                lock (_updateLock) { _updateInProgress = false; }
                return "ERROR: Formulario principal no inicializado";
            }

            LogUtil.LogInfo($"Actualización iniciada para la instancia en el puerto {_tcpPort}", "WebApiExtensions");

            _main.BeginInvoke((System.Windows.Forms.MethodInvoker)(async () =>
            {
                try
                {
                    var (updateAvailable, _, newVersion) = await UpdateChecker.CheckForUpdatesAsync(false);
                    if (updateAvailable || true) // Always allow update when triggered remotely
                    {
                        var updateForm = new UpdateForm(false, newVersion ?? "latest", true);
                        updateForm.PerformUpdate();
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogError($"Error durante la actualización: {ex.Message}", "WebApiExtensions");
                }
            }));

            return "OK: Actualización iniciada";
        }
        catch (Exception ex)
        {
            lock (_updateLock) { _updateInProgress = false; }
            return $"ERROR: {ex.Message}";
        }
    }

    private static string TriggerSelfRestart()
    {
        try
        {
            if (_main == null)
                return "ERROR: Formulario principal no inicializado";

            Task.Run(async () =>
            {
                await Task.Delay(2000);
                _main.BeginInvoke((System.Windows.Forms.MethodInvoker)(() =>
                {
                    Application.Restart();
                }));
            });

            return "OK: Reinicio iniciado";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    private static string GetRestartSchedule()
    {
        try
        {
            var config = RestartManager.GetScheduleConfig();
            var nextRestart = RestartManager.NextScheduledRestart;
            
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                config.Enabled,
                config.Time,
                NextRestart = nextRestart?.ToString("yyyy-MM-dd HH:mm:ss"),
                IsRestartInProgress = RestartManager.IsRestartInProgress,
                CurrentState = RestartManager.CurrentState.ToString()
            });
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    private static string ExecuteGlobalCommand(BotControlCommand command)
    {
        try
        {
            ExecuteMainFormMethod("SendAll", command);
            return $"OK: Comando {command} enviado a todos los bots";
        }
        catch (Exception ex)
        {
            return $"ERROR: Falló la ejecución de {command} - {ex.Message}";
        }
    }

    private static void ExecuteMainFormMethod(string methodName, params object[] args)
    {
        _main!.BeginInvoke((System.Windows.Forms.MethodInvoker)(() =>
        {
            var method = _main.GetType().GetMethod(methodName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method?.Invoke(_main, args);
        }));
    }

    private static string GetBotsList()
    {
        try
        {
            var botList = new List<object>();
            var config = GetConfig();
            var controllers = GetBotControllers();

            if (controllers.Count == 0)
            {
                var botsProperty = _main!.GetType().GetProperty("Bots",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (botsProperty?.GetValue(_main) is List<PokeBotState> bots)
                {
                    foreach (var bot in bots)
                    {
                        botList.Add(new
                        {
                            Id = $"{bot.Connection.IP}:{bot.Connection.Port}",
                            Name = bot.Connection.IP,
                            RoutineType = bot.InitialRoutine.ToString(),
                            Status = "Unknown",
                            ConnectionType = bot.Connection.Protocol.ToString(),
                            bot.Connection.IP,
                            bot.Connection.Port
                        });
                    }

                    return System.Text.Json.JsonSerializer.Serialize(new { Bots = botList });
                }
            }

            foreach (var controller in controllers)
            {
                var state = controller.State;
                var botName = GetBotName(state, config);
                var status = controller.ReadBotState();

                botList.Add(new
                {
                    Id = $"{state.Connection.IP}:{state.Connection.Port}",
                    Name = botName,
                    RoutineType = state.InitialRoutine.ToString(),
                    Status = status,
                    ConnectionType = state.Connection.Protocol.ToString(),
                    state.Connection.IP,
                    state.Connection.Port
                });
            }

            return System.Text.Json.JsonSerializer.Serialize(new { Bots = botList });
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error al obtener la lista de bots: {ex.Message}", "WebAPI");
            return $"ERROR: Falló al obtener la lista de bots - {ex.Message}";
        }
    }

    private static string GetBotStatuses(string? botId)
    {
        try
        {
            var config = GetConfig();
            var controllers = GetBotControllers();

            if (string.IsNullOrEmpty(botId))
            {
                var statuses = controllers.Select(c => new
                {
                    Id = $"{c.State.Connection.IP}:{c.State.Connection.Port}",
                    Name = GetBotName(c.State, config),
                    Status = c.ReadBotState()
                }).ToList();

                return System.Text.Json.JsonSerializer.Serialize(statuses);
            }

            var botController = controllers.FirstOrDefault(c =>
                $"{c.State.Connection.IP}:{c.State.Connection.Port}" == botId);

            return botController?.ReadBotState() ?? "ERROR: Bot no encontrado";
        }
        catch (Exception ex)
        {
            return $"ERROR: Falló al obtener el estado - {ex.Message}";
        }
    }

    private static string CheckReady()
    {
        try
        {
            var controllers = GetBotControllers();
            var hasRunningBots = controllers.Any(c => c.GetBot()?.IsRunning ?? false);
            return hasRunningBots ? "READY" : "NOT_READY";
        }
        catch
        {
            return "NOT_READY";
        }
    }

    private static string GetInstanceInfo()
    {
        try
        {
            var config = GetConfig();
            var version = GetVersion();
            var mode = config?.Mode.ToString() ?? "Desconocido";
            var name = GetInstanceName(config, mode);

            var info = new
            {
                Version = version,
                Mode = mode,
                Name = name,
                Environment.ProcessId,
                Port = _tcpPort,
                ProcessPath = Environment.ProcessPath
            };

            return System.Text.Json.JsonSerializer.Serialize(info);
        }
        catch (Exception ex)
        {
            return $"ERROR: Falló al obtener la información de la instancia - {ex.Message}";
        }
    }
    
    private static string HandleRemoteButton(string[] parts)
    {
        try
        {
            if (parts.Length < 3)
                return "ERROR: Formato de comando inválido. Se esperaba REMOTE_BUTTON:boton:indiceBot";

            var button = parts[1];
            if (!int.TryParse(parts[2], out var botIndex))
                return "ERROR: Índice de bot inválido";

            var controllers = GetBotControllers();
            if (botIndex < 0 || botIndex >= controllers.Count)
                return $"ERROR: Índice de bot {botIndex} fuera de rango";

            var botController = controllers[botIndex];
            var botSource = botController.GetBot();

            if (botSource?.Bot == null)
                return $"ERROR: Bot en el índice {botIndex} no disponible";

            if (!botSource.IsRunning)
                return $"ERROR: Bot en el índice {botIndex} no está en ejecución";

            var bot = botSource.Bot;
            if (bot.Connection is not ISwitchConnectionAsync connection)
                return "ERROR: Conexión del bot no disponible";

            var switchButton = MapButtonToSwitch(button);
            if (switchButton == null)
                return $"ERROR: Botón inválido: {button}";

            var cmd = SwitchCommand.Click(switchButton.Value);

            // Ejecutar el comando de manera sincrónica ya que estamos en un hilo de fondo
            Task.Run(async () => await connection.SendAsync(cmd, CancellationToken.None)).Wait();

            return $"OK: Botón {button} presionado en el bot {botIndex}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    private static string HandleRemoteMacro(string[] parts)
    {
        try
        {
            if (parts.Length < 3)
                return "ERROR: Formato de comando inválido. Se esperaba REMOTE_MACRO:macro:indiceBot";

            var macro = parts[1];
            if (!int.TryParse(parts[2], out var botIndex))
                return "ERROR: Índice de bot inválido";

            var controllers = GetBotControllers();
            if (botIndex < 0 || botIndex >= controllers.Count)
                return $"ERROR: Índice de bot {botIndex} fuera de rango";

            var botController = controllers[botIndex];
            var botSource = botController.GetBot();

            if (botSource?.Bot == null)
                return $"ERROR: Bot en el índice {botIndex} no disponible";

            if (!botSource.IsRunning)
                return $"ERROR: Bot en el índice {botIndex} no está en ejecución";

            // Por ahora, solo devuelve éxito - la implementación de macros puede añadirse después
            return $"OK: Macro {macro} ejecutada en el bot {botIndex}";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    private static SwitchButton? MapButtonToSwitch(string button)
    {
        return button.ToUpperInvariant() switch
        {
            "A" => SwitchButton.A,
            "B" => SwitchButton.B,
            "X" => SwitchButton.X,
            "Y" => SwitchButton.Y,
            "UP" => SwitchButton.DUP,
            "DOWN" => SwitchButton.DDOWN,
            "LEFT" => SwitchButton.DLEFT,
            "RIGHT" => SwitchButton.DRIGHT,
            "L" => SwitchButton.L,
            "R" => SwitchButton.R,
            "ZL" => SwitchButton.ZL,
            "ZR" => SwitchButton.ZR,
            "LSTICK" => SwitchButton.LSTICK,
            "RSTICK" => SwitchButton.RSTICK,
            "HOME" => SwitchButton.HOME,
            "CAPTURE" => SwitchButton.CAPTURE,
            "PLUS" => SwitchButton.PLUS,
            "MINUS" => SwitchButton.MINUS,
            _ => null
        };
    }

    private static List<BotController> GetBotControllers()
    {
        var flpBotsField = _main!.GetType().GetField("FLP_Bots",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (flpBotsField?.GetValue(_main) is FlowLayoutPanel flpBots)
        {
            return [.. flpBots.Controls.OfType<BotController>()];
        }

        return [];
    }

    private static ProgramConfig? GetConfig()
    {
        var configProp = _main?.GetType().GetProperty("Config",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return configProp?.GetValue(_main) as ProgramConfig;
    }

    private static string GetBotName(PokeBotState state, ProgramConfig? config)
    {
        return state.Connection.IP;
    }

    private static string GetVersion()
    {
        return PokeNexo.Version;
    }

    private static string GetInstanceName(ProgramConfig? config, string mode)
    {
        if (!string.IsNullOrEmpty(config?.Hub?.BotName))
            return config.Hub.BotName;

        return mode switch
        {
            "LGPE" => "LGPE",
            "BDSP" => "BDSP",
            "SWSH" => "SWSH",
            "SV" => "SV",
            "LA" => "LA",
            _ => "PokeNexo"
        };
    }

    private static void CreatePortFile()
    {
        try
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
            var exeDir = Path.GetDirectoryName(exePath) ?? Program.WorkingDirectory;
            var portFile = Path.Combine(exeDir, $"PokeNexo_{Environment.ProcessId}.port");
            var tempFile = portFile + ".tmp";

            using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(fs))
            {
                writer.WriteLine(_tcpPort);
                // No longer writing web port - slaves don't have web servers
                writer.Flush();
                fs.Flush(true);
            }

            File.Move(tempFile, portFile, true);
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Failed to create port file: {ex.Message}", "WebServer");
        }
    }

    private static void CleanupPortFile()
    {
        try
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
            var exeDir = Path.GetDirectoryName(exePath) ?? Program.WorkingDirectory;
            var portFile = Path.Combine(exeDir, $"PokeNexo_{Environment.ProcessId}.port");

            if (File.Exists(portFile))
                File.Delete(portFile);
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error al limpiar el archivo de puerto: {ex.Message}", "WebServer");
        }
    }

    private static int FindAvailablePort(int startPort)
    {
        var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
        var exeDir = Path.GetDirectoryName(exePath) ?? Program.WorkingDirectory;

        // Use a lock to prevent race conditions
        lock (_portLock)
        {
            for (int port = startPort; port < startPort + 100; port++)
            {
                // Check if port is reserved by another instance
                if (_portReservations.ContainsKey(port))
                    continue;

                if (!IsPortInUse(port))
                {
                    // Check if any port file claims this port
                    var portFiles = Directory.GetFiles(exeDir, "PokeNexo_*.port");
                    bool portClaimed = false;

                    foreach (var file in portFiles)
                    {
                        try
                        {
                            // Lock the file before reading to prevent race conditions
                            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                            using var reader = new StreamReader(fs);
                            var content = reader.ReadToEnd().Trim();
                            if (content == port.ToString() || content.Contains($"\"Port\":{port}"))
                            {
                                portClaimed = true;
                                break;
                            }
                        }
                        catch { }
                    }

                    if (!portClaimed)
                    {
                        // Double-check the port is still available
                        if (!IsPortInUse(port))
                        {
                            return port;
                        }
                    }
                }
            }
        }
        throw new InvalidOperationException("No se encontraron puertos disponibles");
    }

    private static bool IsPortInUse(int port)
    {
        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMilliseconds(200) };
            var response = client.GetAsync($"http://localhost:{port}/api/bot/instances").Result;
            return response.IsSuccessStatusCode;
        }
        catch
        {
            try
            {
                using var tcpClient = new TcpClient();
                var result = tcpClient.BeginConnect("127.0.0.1", port, null, null);
                var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(200));
                if (success)
                {
                    tcpClient.EndConnect(result);
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }

    public static void StopWebServer(this Main mainForm)
    {
        try
        {
            _monitorCts?.Cancel();
            _cts?.Cancel();
            _tcp?.Stop();
            _server?.Dispose();
            RestartManager.Shutdown();

            // Release the port reservations
            lock (_portLock)
            {
                ReleasePort(_tcpPort);
            }

            CleanupPortFile();
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error al detener el servidor web: {ex.Message}", "WebServer");
        }
    }

    private static void CheckPostRestartStartup(Main mainForm)
    {
        try
        {
            var workingDir = Path.GetDirectoryName(Application.ExecutablePath) ?? Environment.CurrentDirectory;
            var restartFlagPath = Path.Combine(workingDir, "restart_in_progress.flag");
            var updateFlagPath = Path.Combine(workingDir, "update_in_progress.flag");

            bool isPostRestart = File.Exists(restartFlagPath);
            bool isPostUpdate = File.Exists(updateFlagPath);

            if (!isPostRestart && !isPostUpdate)
                return;

            string operation = isPostRestart ? "reinicio" : "actualización";
            string logSource = isPostRestart ? "RestartManager" : "UpdateManager";

            LogUtil.LogInfo($"Inicio posterior a {operation} detectado. Esperando a que todas las instancias estén en línea...", logSource);

            if (isPostRestart) File.Delete(restartFlagPath);
            if (isPostUpdate) File.Delete(updateFlagPath);

            Task.Run(() => HandlePostOperationStartupAsync(mainForm, operation, logSource));
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error al verificar el inicio posterior a reinicio/actualización: {ex.Message}", "StartupManager");
        }
    }

    private static async Task HandlePostOperationStartupAsync(Main mainForm, string operation, string logSource)
    {
        await Task.Delay(5000);
        
        const int maxAttempts = 12;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                LogUtil.LogInfo($"Intento de verificación post-{operation} {attempt + 1}/{maxAttempts}", logSource);

                // Iniciar bots locales
                ExecuteMainFormMethod("SendAll", BotControlCommand.Start);
                LogUtil.LogInfo("Comando Start All enviado a los bots locales", logSource);

                // Iniciar instancias remotas
                var instances = GetAllRunningInstances(0);
                if (instances.Count > 0)
                {
                    LogUtil.LogInfo($"Se encontraron {instances.Count} instancias remotas en línea. Enviando comando Start All...", logSource);
                    await SendStartCommandsToRemoteInstancesAsync(instances, logSource);
                }

                LogUtil.LogInfo($"Comandos Start All post-{operation} completados con éxito", logSource);
                break;
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"Error durante el intento {attempt + 1} del inicio post-{operation}: {ex.Message}", logSource);
                if (attempt < maxAttempts - 1)
                    await Task.Delay(5000);
            }
        }
    }
    
    private static async Task SendStartCommandsToRemoteInstancesAsync(List<(int Port, int ProcessId)> instances, string logSource)
    {
        var tasks = instances.Select(instance => Task.Run(() =>
        {
            try
            {
                var response = BotServer.QueryRemote(instance.Port, "STARTALL");
                LogUtil.LogInfo($"Comando Start enviado al puerto {instance.Port}: {response}", logSource);
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"Error al enviar el comando Start al puerto {instance.Port}: {ex.Message}", logSource);
            }
        }));
        
        await Task.WhenAll(tasks);
    }

    private static List<(int Port, int ProcessId)> GetAllRunningInstances(int currentPort)
    {
        var instances = new List<(int, int)>();

        try
        {
            var processes = Process.GetProcessesByName("PokeNexo")
                .Where(p => p.Id != Environment.ProcessId);

            foreach (var process in processes)
            {
                try
                {
                    var exePath = process.MainModule?.FileName;
                    if (string.IsNullOrEmpty(exePath))
                        continue;

                    var portFile = Path.Combine(Path.GetDirectoryName(exePath)!, $"PokeNexo_{process.Id}.port");
                    if (!File.Exists(portFile))
                        continue;

                    var portText = File.ReadAllText(portFile).Trim();
                    // Port file now contains TCP port on first line, web port on second line (for slaves)
                    var lines = portText.Split('\n', '\r').Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
                    if (lines.Length == 0 || !int.TryParse(lines[0], out var port))
                        continue;

                    if (IsPortInUse(port))
                    {
                        instances.Add((port, process.Id));
                    }
                }
                catch { }
            }
        }
        catch { }

        return instances;
    }

}
