using SysBot.Base;
using SysBot.Pokemon.Discord;
using SysBot.Pokemon.WinForms.Helpers;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SysBot.Pokemon.WinForms
{
    public partial class BotController : UserControl
    {
        private bool _suspendPainting = false;
        private volatile bool _hasPendingStateUpdate = false;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public PokeBotState State { get; private set; } = new();
        private IPokeBotRunner? Runner;
        public EventHandler? Remove;

        private Color currentStatusColor = Color.FromArgb(90, 186, 71);
        private DateTime LastUpdateStatus = DateTime.Now;
        private bool buttonHovering = false;
        private float pulseScale = 1.0f;
        private bool pulseGrowing = true;
        private const float MIN_PULSE_SCALE = 0.6f;
        private const float MAX_PULSE_SCALE = 1.0f;
        private const float PULSE_SPEED = 0.03f;

        private readonly Color CuztomBackground = Color.FromArgb(27, 40, 56);
        private readonly Color CuztomDarkBackground = Color.FromArgb(22, 32, 45);
        private readonly Color CuztomDarkerBackground = Color.FromArgb(16, 24, 34);
        private readonly Color CuztomAccent = Color.FromArgb(102, 192, 244);
        private readonly Color CuztomText = Color.FromArgb(239, 239, 239);
        private readonly Color CuztomSubText = Color.FromArgb(139, 179, 217);
        private readonly Color CuztomGreen = Color.FromArgb(90, 186, 71);
        private readonly Color CuztomRed = Color.FromArgb(236, 98, 95);
        private readonly Color CuztomYellow = Color.FromArgb(245, 197, 92);
        private readonly Color CuztomOrange = Color.FromArgb(251, 176, 64);

        public BotController()
        {
            InitializeComponent();
            SetStyle(ControlStyles.AllPaintingInWmPaint | 
                    ControlStyles.UserPaint |
                    ControlStyles.DoubleBuffer | 
                    ControlStyles.ResizeRedraw |
                    ControlStyles.OptimizedDoubleBuffer | 
                    ControlStyles.SupportsTransparentBackColor |
                    ControlStyles.Opaque, true);
            UpdateStyles();
            
            // Skip initialization in design mode
            if (DesignMode || System.ComponentModel.LicenseManager.UsageMode == System.ComponentModel.LicenseUsageMode.Designtime)
                return;

            ConfigureContextMenu();
            ConfigureChildControls();
            ModernizeStatusIndicator();
            ConfigureButtonAppearance();
            InitializeAnimationTimer();
        }
        
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            
            // Skip if in design mode or disposed
            if (DesignMode || IsDisposed)
                return;
                
            if (Visible)
            {
                // When becoming visible, ensure animations are running
                ResumeAnimations();
            }
            else
            {
                // When hidden, pause animations to save resources
                PauseAnimations();
            }
        }
        
        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            
            // Skip if in design mode or disposed
            if (DesignMode || IsDisposed)
                return;
                
            // Ensure timer is running when control is added to a parent
            if (Parent != null && Visible)
            {
                ResumeAnimations();
            }
        }

        private void ModernizeStatusIndicator()
        {
            // Scale-aware sizing for smaller circle
            var dpiScale = DeviceDpi / 96f;
            var scaledSize = (int)(16 * dpiScale); // Reduced from 24 to 16
            statusIndicator.Size = new Size(scaledSize, scaledSize);
            statusIndicator.Location = new Point((int)(12 * dpiScale), (int)(25 * dpiScale)); // Adjusted vertical position
            statusIndicator.BackColor = Color.Transparent;
        }

        private void ConfigureButtonAppearance()
        {
            btnActions.Text = "\u27a4 MENU BOT";
            btnActions.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            btnActions.ForeColor = Color.White;
            btnActions.FlatStyle = FlatStyle.Flat;
            btnActions.FlatAppearance.BorderSize = 0;
            btnActions.Cursor = Cursors.Hand;
        }

        private void InitializeAnimationTimer()
        {
            if (animationTimer is null)
            {
                animationTimer = new System.Windows.Forms.Timer
                {
                    Interval = 30 // 30ms for smooth animation (~33 FPS)
                };
                animationTimer.Tick += AnimationTimer_Tick;
            }
            if (!animationTimer.Enabled)
            {
                animationTimer.Start();
            }
        }

        private void ConfigureContextMenu()
        {
            var opt = (BotControlCommand[])Enum.GetValues(typeof(BotControlCommand));

            contextMenu.Renderer = new CuztomMenuRenderer();

            for (int i = 1; i < opt.Length; i++)
            {
                var cmd = opt[i];
                var item = new ToolStripMenuItem(cmd.ToString())
                {
                    ForeColor = Color.White,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 9F)
                };
                item.Click += (_, __) => SendCommand(cmd);

                switch (cmd)
                {
                    case BotControlCommand.Start:
                        item.Text = "▶  Iniciar";
                        break;
                    case BotControlCommand.Stop:
                        item.Text = "■  Detener";
                        break;
                    case BotControlCommand.Idle:
                        item.Text = "❚❚  Inactivo";
                        break;
                    case BotControlCommand.Resume:
                        item.Text = "⏵  Reanudar";
                        break;
                    case BotControlCommand.Restart:
                        item.Text = "↻  Reiniciar";
                        break;
                    case BotControlCommand.RebootAndStop:
                        item.Text = "⚡  Reiniciar y Detener";
                        break;
                    case BotControlCommand.ScreenOnAll:
                        item.Text = "☀  Encender Pantalla";
                        break;
                    case BotControlCommand.ScreenOffAll:
                        item.Text = "🌙  Apagar Pantalla";
                        break;
                }

                contextMenu.Items.Add(item);
            }

            contextMenu.Items.Add(new ToolStripSeparator());

            // Add recovery status item
            var recoveryItem = new ToolStripMenuItem("📊 Estado de Recuperación")
            {
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F)
            };
            recoveryItem.Click += ShowRecoveryStatus;
            contextMenu.Items.Add(recoveryItem);

            var remove = new ToolStripMenuItem("╳  Eliminar Bot")
            {
                ForeColor = CuztomRed,
                BackColor = CuztomDarkBackground,
                Font = new Font("Segoe UI", 8.5F)
            };
            remove.Click += (_, __) => TryRemove();
            contextMenu.Items.Add(remove);
            contextMenu.Opening += RcMenuOnOpening;

            RCMenu = contextMenu;
        }

        private void ConfigureChildControls()
        {
            foreach (var c in Controls.OfType<Control>())
            {
                if (c != btnActions)
                {
                    c.MouseEnter += BotController_MouseEnter;
                    c.MouseLeave += BotController_MouseLeave;
                }
            }

            foreach (var c in mainPanel.Controls.OfType<Control>())
            {
                c.MouseEnter += BotController_MouseEnter;
                c.MouseLeave += BotController_MouseLeave;
            }
        }

        private void RcMenuOnOpening(object? sender, CancelEventArgs e)
        {
            if (Runner is null)
                return;

            var bot = Runner.GetBot(State);
            if (bot is null)
                return;

            foreach (var tsi in contextMenu.Items.OfType<ToolStripMenuItem>())
            {
                if (tsi.Text != null && tsi.Text.Length >= 3)
                {
                    var text = tsi.Text[3..].Trim();
                    tsi.Enabled = Enum.TryParse(text.Replace(" ", "").Replace("&", "And"), out BotControlCommand cmd)
                        ? cmd.IsUsable(bot.IsRunning, bot.IsPaused)
                        : !bot.IsRunning;
                }
            }
        }

        public void Initialize(IPokeBotRunner runner, PokeBotState cfg)
        {
            Runner = runner;
            State = cfg;
            ReloadStatus();
            lblConnectionInfo.Text = "Iniciando...";
        }

        public void ReloadStatus()
        {
            var bot = GetBot()?.Bot;
            if (bot is null) return;
            // Bot name with TID format on second line
            lblBotName.Text = $"{bot.Connection.Name}-{bot.Connection.Label}";
            // Trade type will be updated in ReloadStatus(BotSource) with current time
            lblRoutineType.Text = $"{State.InitialRoutine}";
            lblRoutineType.Visible = true;
            L_Left.Text = $"{bot.Connection.Name}\n{State.InitialRoutine}";
        }

        public void ReloadStatus(BotSource<PokeBotState> b)
        {
            ReloadStatus();
            var bot = b.Bot;

            // Line 2: Bot name with TID format
            lblBotName.Text = $"{bot.Connection.Name}-{bot.Connection.Label}";

            // Line 3: Trade type with current time (12-hour format)
            var routineType = bot.Config.CurrentRoutineType == PokeRoutineType.Idle ?
                State.InitialRoutine.ToString() : bot.Config.CurrentRoutineType.ToString();
            lblRoutineType.Text = $"{routineType} @ {DateTime.Now:h:mm:ss tt}";

            // Line 4: Current activity with arrow
            lblConnectionInfo.Text = $"\u21aa {bot.LastLogged}";

            var botState = ReadBotState();

            // Normalizamos a inglés para el switch
            static string NormalizeState(string s)
            {
                var t = (s ?? "").Trim().ToUpperInvariant();
                return t switch
                {
                    "DETENIDO" => "STOPPED",
                    "DETENIÉNDOSE" => "STOPPING",
                    "INACTIVO" => "IDLE",
                    "EN ESPERA" => "IDLING",
                    "REINICIANDO" => "REBOOTING",
                    "DESCONOCIDO" => "UNKNOWN",
                    _ => t, // "ERROR", o nombre de rutina
                };
            }

            var key = NormalizeState(botState);

            // Line 1: Status text next to pulsing indicator
            lblStatusValue.Text = botState.ToUpper();

            // Check for recovery status
            var recoveryState = b.GetRecoveryState();
            if (recoveryState is { ConsecutiveFailures: > 0 })
            {
                lblConnectionInfo.Text += $" [Intentos de recuperación: {recoveryState.ConsecutiveFailures}]";
            }

            switch (key)
            {
                case "STOPPED":
                    currentStatusColor = Color.FromArgb(100, 100, 100);
                    lblStatusValue.ForeColor = Color.FromArgb(100, 100, 100);
                    if (recoveryState is { IsRecovering: true })
                    {
                        currentStatusColor = CuztomOrange;
                        lblStatusValue.ForeColor = CuztomOrange;
                        lblStatusValue.Text = "RECUPERANDO";
                    }
                    else
                    {
                        lblStatusValue.Text = "DETENIDO";
                    }
                    break;

                case "IDLE":
                case "IDLING":
                    currentStatusColor = CuztomYellow;
                    lblStatusValue.ForeColor = CuztomYellow;
                    lblStatusValue.Text = "INACTIVO";
                    break;

                case "ERROR":
                    currentStatusColor = CuztomRed;
                    lblStatusValue.ForeColor = CuztomRed;
                    lblStatusValue.Text = "ERROR";
                    break;

                case "REBOOTING":
                    currentStatusColor = CuztomAccent;
                    lblStatusValue.ForeColor = CuztomAccent;
                    lblStatusValue.Text = "REINICIANDO";
                    break;

                case "UNKNOWN":
                    currentStatusColor = CuztomAccent;
                    lblStatusValue.ForeColor = CuztomAccent;
                    lblStatusValue.Text = "DESCONOCIDO";
                    break;

                default:
                    currentStatusColor = CuztomGreen;
                    lblStatusValue.ForeColor = CuztomGreen;
                    lblStatusValue.Text = "EJECUTANDO";
                    break;
            }

            statusIndicator.Invalidate();

            var lastTime = bot.LastTime;
            if (!b.IsRunning)
            {
                currentStatusColor = Color.FromArgb(100, 100, 100);
                statusIndicator.Invalidate();
                return;
            }

            if (!b.Bot.Connection.Connected)
            {
                currentStatusColor = CuztomAccent;
                statusIndicator.Invalidate();
                return;
            }

            var cfg = bot.Config;
            if (cfg.CurrentRoutineType == PokeRoutineType.Idle && cfg.NextRoutineType == PokeRoutineType.Idle)
            {
                currentStatusColor = CuztomYellow;
                statusIndicator.Invalidate();
                return;
            }

            if (LastUpdateStatus == lastTime)
                return;

            const int threshold = 100;
            Color good = cfg.Connection.Protocol == SwitchProtocol.USB ? CuztomAccent : CuztomGreen;
            Color bad = CuztomRed;

            var delta = DateTime.Now - lastTime;
            var seconds = delta.Seconds;

            LastUpdateStatus = lastTime;
            if (seconds > 2 * threshold)
            {
                statusIndicator.Invalidate();
                return;
            }

            if (seconds > threshold)
            {
                currentStatusColor = bad;
            }
            else
            {
                var factor = seconds / (double)threshold;
                currentStatusColor = Blend(bad, good, factor * factor);
            }
            
            statusIndicator.Invalidate();
        }

        private static Color Blend(Color color, Color backColor, double amount)
        {
            byte r = (byte)((color.R * amount) + (backColor.R * (1 - amount)));
            byte g = (byte)((color.G * amount) + (backColor.G * (1 - amount)));
            byte b = (byte)((color.B * amount) + (backColor.B * (1 - amount)));
            return Color.FromArgb(r, g, b);
        }

        public void TryRemove()
        {
            var bot = GetBot();
            if (!Runner!.Config.SkipConsoleBotCreation)
                bot?.Stop();

            Remove?.Invoke(this, EventArgs.Empty);
        }

        public void SendCommand(BotControlCommand cmd, bool echo = true)
        {
            if (Runner?.Config.SkipConsoleBotCreation != false)
            {
                LogUtil.LogError("No se crearon bots porque SkipConsoleBotCreation está activado.", "Hub");
                return;
            }
            var bot = GetBot();
            if (bot is null)
            {
                LogUtil.LogError("El bot es nulo.", "BotController");
                return;
            }

            switch (cmd)
            {
                case BotControlCommand.Idle:
                    bot.Pause();
                    break;
                case BotControlCommand.Start:
                    Runner.InitializeStart();
                    bot.Start();
                    break;
                case BotControlCommand.Stop:
                    bot.Stop();
                    break;
                case BotControlCommand.Resume:
                    bot.Resume();
                    break;
                case BotControlCommand.RebootAndStop:
                    bot.RebootAndStop();
                    break;
                case BotControlCommand.Restart:
                    {
                        var prompt = WinFormsUtil.Prompt(MessageBoxButtons.YesNo, "¿Estás seguro de que deseas reiniciar la conexión?");
                        if (prompt != DialogResult.Yes)
                            return;

                        // Stop the bot first to ensure proper cleanup
                        bot.Stop();
                        
                        // Use async delay instead of blocking the UI thread
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(500); // Give it time to stop properly
                            
                            // Use BeginInvoke to update UI from background thread
                            if (!IsDisposed)
                            {
                                BeginInvoke((MethodInvoker)(() =>
                                {
                                    Runner.InitializeStart();
                                    bot.Bot.Connection.Reset();
                                    bot.Start();
                                }));
                            }
                        });
                        break;
                    }
                case BotControlCommand.ScreenOnAll:
                    ExecuteScreenCommand(true);
                    break;
                case BotControlCommand.ScreenOffAll:
                    ExecuteScreenCommand(false);
                    break;
                default:
                    WinFormsUtil.Alert($"{cmd} no es un comando que se pueda enviar al Bot.");
                    return;
            }
        }

        private void ExecuteScreenCommand(bool screenOn)
        {
            if (Runner is null)
            {
                LogUtil.LogError("Runner es nulo - no se puede ejecutar el comando de pantalla", "BotController");
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    var bots = Runner.Bots;
                    if (bots is null or { Count: 0 })
                    {
                        LogUtil.LogError("No hay bots disponibles para ejecutar el comando de pantalla", "BotController");
                        return;
                    }

                    int successCount = 0;
                    int totalCount = bots.Count;

                    foreach (var botSource in bots)
                    {
                        try
                        {
                            var bot = botSource.Bot;
                            if (bot?.Connection != null && bot.Connection.Connected)
                            {
                                var crlf = bot is SwitchRoutineExecutor<PokeBotState> { UseCRLF: true };
                                await bot.Connection.SendAsync(SwitchCommand.SetScreen(screenOn ? ScreenState.On : ScreenState.Off, crlf), CancellationToken.None);
                                successCount++;
                                LogUtil.LogInfo($"Pantalla {(screenOn ? "ENCENDIDA" : "APAGADA")} para {bot.Connection.Name}", "BotController");
                            }
                            else
                            {
                                LogUtil.LogError($"No se puede enviar el comando de pantalla - el bot {bot?.Connection?.Name ?? "desconocido"} no está conectado", "BotController");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogUtil.LogError($"Error al enviar el comando de pantalla al bot: {ex.Message}", "BotController");
                        }
                    }

                    LogUtil.LogInfo($"Comando de pantalla enviado a {successCount} de {totalCount} bots", "BotController");
                }
                catch (Exception ex)
                {
                    LogUtil.LogError($"Error al ejecutar el comando de pantalla para todos los bots: {ex.Message}", "BotController");
                }
            });
        }

        private void ShowRecoveryStatus(object? sender, EventArgs e)
        {
            var bot = GetBot();
            if (bot is null)
            {
                MessageBox.Show("Bot no encontrado.", "Estado de Recuperación", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var recoveryState = bot.GetRecoveryState();
            if (recoveryState is null)
            {
                MessageBox.Show("El servicio de recuperación no está habilitado para este bot.", "Estado de Recuperación", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var status = $"Bot: {bot.Bot.Connection.Name}\n" +
                         $"Estado: {(bot.IsRunning ? "En ejecución" : "Detenido")}\n" +
                         $"Intentos de recuperación: {recoveryState.ConsecutiveFailures}\n" +
                         $"Total de fallos: {recoveryState.CrashHistory.Count}\n" +
                         $"¿Recuperándose?: {(recoveryState.IsRecovering ? "Sí" : "No")}\n";

            if (recoveryState.LastRecoveryAttempt is not null)
            {
                status += $"Última recuperación: {recoveryState.LastRecoveryAttempt.Value:yyyy-MM-dd HH:mm:ss}\n";
            }

            if (recoveryState.CrashHistory.Count > 0)
            {
                var lastCrash = recoveryState.CrashHistory.OrderByDescending(c => c).FirstOrDefault();
                if (lastCrash != default)
                {
                    status += $"Último fallo: {lastCrash:yyyy-MM-dd HH:mm:ss}\n";
                }
            }

            MessageBox.Show(status, "Estado de Recuperación", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public string ReadBotState()
        {
            try
            {
                var botSource = GetBot();
                if (botSource is null)
                    return "ERROR";

                var bot = botSource.Bot;
                if (bot is null)
                    return "ERROR";

                if (!botSource.IsRunning)
                    return "DETENIDO";

                if (botSource.IsStopping)
                    return "DETENIÉNDOSE";

                if (botSource.IsPaused)
                {
                    if (bot.Config?.CurrentRoutineType != PokeRoutineType.Idle)
                        return "EN ESPERA";
                    else
                        return "INACTIVO";
                }

                if (botSource.IsRunning && !bot.Connection.Connected)
                    return "REINICIANDO";

                var cfg = bot.Config;
                if (cfg == null)
                    return "DESCONOCIDO";

                if (cfg.CurrentRoutineType == PokeRoutineType.Idle)
                    return "INACTIVO";

                if (botSource.IsRunning && bot.Connection.Connected)
                    return cfg.CurrentRoutineType.ToString();

                return "DESCONOCIDO";
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"Error al leer el estado del bot: {ex.Message}", "BotController");
                return "ERROR";
            }
        }

        public BotSource<PokeBotState>? GetBot()
        {
            try
            {
                if (Runner is null)
                    return null;

                var bot = Runner.GetBot(State);
                if (bot is null)
                    return null;

                return bot;
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"Error al obtener el bot: {ex.Message}", "BotController");
                return null;
            }
        }

        private void BotController_MouseEnter(object? sender, EventArgs e)
        {
            // Mouse hover removed for simplicity
        }

        private void BotController_MouseLeave(object? sender, EventArgs e)
        {
            // Mouse hover removed for simplicity
        }

        private void BtnActions_MouseEnter(object? sender, EventArgs e)
        {
            buttonHovering = true;
        }

        private void BtnActions_MouseLeave(object? sender, EventArgs e)
        {
            buttonHovering = false;
        }

        public void ReadState()
        {
            if (_suspendPainting || IsDisposed) return;
            
            var bot = GetBot();
            if (bot is null) return;

            if (InvokeRequired)
            {
                // Use BeginInvoke to avoid blocking the calling thread
                // Check if we already have a pending update to avoid queuing multiple updates
                if (!_hasPendingStateUpdate)
                {
                    _hasPendingStateUpdate = true;
                    BeginInvoke((System.Windows.Forms.MethodInvoker)(() => 
                    {
                        _hasPendingStateUpdate = false;
                        if (!_suspendPainting && !IsDisposed)
                        {
                            try
                            {
                                ReloadStatus(bot);
                            }
                            catch (Exception ex)
                            {
                                LogUtil.LogError($"Error al actualizar el estado del bot: {ex.Message}", "BotController");
                            }
                        }
                    }));
                }
            }
            else
            {
                if (!IsDisposed)
                {
                    try
                    {
                        ReloadStatus(bot);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError($"Error al actualizar el estado del bot: {ex.Message}", "BotController");
                    }
                }
            }
        }

        public void PauseAnimations()
        {
            _suspendPainting = true;
            if (animationTimer is not null)
                animationTimer.Stop();
        }

        public void ResumeAnimations()
        {
            _suspendPainting = false;
            if (animationTimer is not null && !animationTimer.Enabled)
            {
                animationTimer.Start();
            }
            // Force a refresh of status when resuming
            ReadState();
            statusIndicator?.Invalidate();
        }

        private void BotController_Paint(object sender, PaintEventArgs e)
        {
            if (_suspendPainting) return;
            
            var g = e.Graphics;
            // Paint the outer container with consistent background
            using (var bgBrush = new SolidBrush(CuztomBackground))
            {
                g.FillRectangle(bgBrush, ClientRectangle);
            }
        }

        private void MainPanel_Paint(object sender, PaintEventArgs e)
        {
            if (_suspendPainting) return;
            
            var g = e.Graphics;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            var rect = e.ClipRectangle;
            
            // Paint with the same background as the main form
            using (var bgBrush = new SolidBrush(CuztomDarkBackground))
            {
                g.FillRectangle(bgBrush, rect);
            }
        }


        private void StatusIndicator_Paint(object sender, PaintEventArgs e)
        {
            if (_suspendPainting) return;
            
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.CompositingQuality = CompositingQuality.HighQuality;

            if (sender is not PictureBox control) return;

            // Don't clear - let parent background show through
            // This ensures transparency works properly

            // Calculate pulsing circle dimensions
            var centerX = control.Width / 2f;
            var centerY = control.Height / 2f;
            var baseRadius = Math.Min(control.Width, control.Height) * 0.35f; // Smaller circle
            var currentRadius = baseRadius * pulseScale;

            // Draw the pulsing circle
            using var path = new GraphicsPath();
            path.AddEllipse(centerX - currentRadius, centerY - currentRadius, currentRadius * 2, currentRadius * 2);

            // Apply opacity based on pulse scale for breathing effect
            var opacity = (int)(255 * (0.7f + 0.3f * pulseScale));
            var colorWithOpacity = Color.FromArgb(opacity, currentStatusColor);
            
            using var brush = new SolidBrush(colorWithOpacity);
            g.FillPath(brush, path);

            // Add subtle glow effect for larger pulse scales
            if (pulseScale > 0.8f)
            {
                using var glowBrush = new SolidBrush(Color.FromArgb((int)(20 * pulseScale), currentStatusColor));
                var glowRadius = currentRadius + 3;
                using var glowPath = new GraphicsPath();
                glowPath.AddEllipse(centerX - glowRadius, centerY - glowRadius, glowRadius * 2, glowRadius * 2);
                g.FillPath(glowBrush, glowPath);
            }
        }

        private void BtnActions_Paint(object sender, PaintEventArgs e)
        {
            if (_suspendPainting) return;
            
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            if (sender is not Button btn) return;
            var rect = btn.ClientRectangle;

            using var path = new GraphicsPath();
            GraphicsExtensions.AddRoundedRectangle(path, rect, 4);
            btn.Region = new Region(path);

            // Simple solid color
            var bgColor = buttonHovering ? Color.FromArgb(120, 200, 255) : Color.FromArgb(102, 192, 244);
            using (var bgBrush = new SolidBrush(bgColor))
            {
                g.FillPath(bgBrush, path);
            }

            // Simple border
            using (var borderPen = new Pen(Color.FromArgb(100, 180, 230), 1))
            {
                g.DrawPath(borderPen, path);
            }

            // Draw text
            var textFormat = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            using var textBrush = new SolidBrush(Color.White);
            g.DrawString(btn.Text, btn.Font, textBrush, rect, textFormat);
        }

        private void BtnActions_Click(object sender, EventArgs e)
        {
            contextMenu.Show(btnActions, new Point(0, btnActions.Height + 1));
        }

        private void AnimationTimer_Tick(object? sender, EventArgs e)
        {
            if (_suspendPainting || IsDisposed) return;

            // Update pulse scale
            if (pulseGrowing)
            {
                pulseScale += PULSE_SPEED;
                if (pulseScale >= MAX_PULSE_SCALE)
                {
                    pulseScale = MAX_PULSE_SCALE;
                    pulseGrowing = false;
                }
            }
            else
            {
                pulseScale -= PULSE_SPEED;
                if (pulseScale <= MIN_PULSE_SCALE)
                {
                    pulseScale = MIN_PULSE_SCALE;
                    pulseGrowing = true;
                }
            }

            // Invalidate only the status indicator for efficient repainting
            if (statusIndicator is not null && !statusIndicator.IsDisposed)
            {
                statusIndicator.Invalidate();
            }
            
            // Update the time display periodically (every second)
            if (DateTime.Now.Subtract(LastUpdateStatus).TotalSeconds >= 1)
            {
                ReadState();
            }
        }

        private class CuztomMenuRenderer : ToolStripProfessionalRenderer
        {
            public CuztomMenuRenderer() : base(new CuztomColorTable()) { }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                var rc = new Rectangle(Point.Empty, e.Item.Size);
                var c = e.Item.Selected ? Color.FromArgb(55, 70, 95) : Color.FromArgb(35, 45, 60);
                using var brush = new SolidBrush(c);
                e.Graphics.FillRectangle(brush, rc);
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                // Force white text for better visibility
                if (!e.Item.Enabled)
                    e.TextColor = Color.FromArgb(120, 120, 120);
                else
                    e.TextColor = Color.White;
                    
                base.OnRenderItemText(e);
            }
        }

        private class CuztomColorTable : ProfessionalColorTable
        {
            public override Color MenuItemSelected => Color.FromArgb(55, 70, 95);
            public override Color MenuItemBorder => Color.FromArgb(80, 120, 160);
            public override Color MenuBorder => Color.FromArgb(20, 30, 40);
            public override Color ToolStripDropDownBackground => Color.FromArgb(35, 45, 60);
            public override Color ImageMarginGradientBegin => Color.FromArgb(35, 45, 60);
            public override Color ImageMarginGradientMiddle => Color.FromArgb(35, 45, 60);
            public override Color ImageMarginGradientEnd => Color.FromArgb(35, 45, 60);
            public override Color SeparatorDark => Color.FromArgb(20, 30, 40);
            public override Color SeparatorLight => Color.FromArgb(55, 65, 80);
        }
    }

    public enum BotControlCommand
    {
        None,
        Start,
        Stop,
        Idle,
        Resume,
        Restart,
        RebootAndStop,
        ScreenOnAll,
        ScreenOffAll,
    }

    public static class BotControlCommandExtensions
    {
        public static bool IsUsable(this BotControlCommand cmd, bool running, bool paused)
        {
            return cmd switch
            {
                BotControlCommand.Start => !running,
                BotControlCommand.Stop => running,
                BotControlCommand.Idle => running && !paused,
                BotControlCommand.Resume => paused,
                BotControlCommand.Restart => true,
                BotControlCommand.ScreenOnAll => running,
                BotControlCommand.ScreenOffAll => running,
                _ => false,
            };
        }
    }
}
