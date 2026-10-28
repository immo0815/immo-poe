namespace ClickableTransparentOverlay
{
    using ClickableTransparentOverlay.Win32;
    using SixLabors.ImageSharp;
    using SixLabors.ImageSharp.PixelFormats;
    using SixLabors.ImageSharp.Formats;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Vortice.Direct3D;
    using Vortice.Direct3D11;
    using Vortice.DXGI;
    using Vortice.Mathematics;
    using Point = System.Drawing.Point;
    using Size = System.Drawing.Size;
    using ImGuiNET;
    using System.Collections.Concurrent;

    /// <summary>
    /// A class to create clickable transparent overlay on windows machine.
    /// </summary>
    public abstract class Overlay : IDisposable
    {
        private readonly string title;
        private readonly Format format;
        private readonly int initialWindowWidth;
        private readonly int initialWindowHeight;

        private WNDCLASSEX wndClass;

        /// <summary>
        ///  Do not assume this class is initialized.
        ///  Consider using this variable only in <see cref="PostInitialized"/> or <see cref="Render"/> function.
        /// </summary>
        public Win32Window window;
        private ID3D11Device device;
        private ID3D11DeviceContext deviceContext;
        private IDXGISwapChain swapChain;
        private ID3D11Texture2D backBuffer;
        private ID3D11RenderTargetView renderView;

        private ImGuiRenderer renderer;
        private ImGuiInputHandler inputhandler;

        private bool _disposedValue;
        private IntPtr selfPointer;
        private Thread renderThread;
        private volatile CancellationTokenSource cancellationTokenSource;
        private volatile bool overlayIsReady;
        private int fpslimit;

        private Dictionary<string, (IntPtr Handle, uint Width, uint Height)> loadedTexturesPtrs;

        private readonly ConcurrentQueue<FontHelper.FontLoadDelegate> fontUpdates;

        // PoEformance: the loop's heartbeat, what Present last answered and the device's rebuild - see NOTICE.md.
        private long framesDone;
        private volatile string stage = "starting";
        private volatile int presentCode;

        /// <summary>The device's removal reason as the render thread last read it, for <see cref="WindowState"/> on any thread.</summary>
        private volatile string removal = string.Empty;
        private long nextRebuild;
        private int rebuilds;

        /// <summary>The views handed over by <see cref="AddView"/>: their owners release them, a rebuild only forgets them.</summary>
        private readonly HashSet<IntPtr> borrowed = new();

        /// <summary>DXGI_STATUS_OCCLUDED: nothing of the window is on show, and Present returns at once rather than waiting for the screen.</summary>
        private const int Occluded = 0x087A0001;

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="Overlay"/> class.
        /// </summary>
        public Overlay() : this("Overlay")
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Overlay"/> class.
        /// </summary>
        /// <param name="windowTitle">
        /// Title of the window created by the overlay
        /// </param>
        public Overlay(string windowTitle) : this(windowTitle, false)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Overlay"/> class.
        /// </summary>
        /// <param name="DPIAware">
        /// should the overlay scale with windows scale value or not.
        /// </param>
        public Overlay(bool DPIAware) : this("Overlay", DPIAware)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Overlay"/> class.
        /// </summary>
        /// <param name="windowTitle">
        /// Title of the window created by the overlay
        /// </param>
        /// <param name="DPIAware">
        /// should the overlay scale with windows scale value or not.
        /// </param>
        public Overlay(string windowTitle, bool DPIAware) : this(windowTitle, DPIAware, 800, 600)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Overlay"/> class.
        /// </summary>
        /// <param name="windowWidth">
        /// width to use when creating the clickable  transparent overlay window
        /// </param>
        /// <param name="windowHeight">
        /// height to use when creating the clickable transparent overlay window
        /// </param>
        public Overlay(int windowWidth, int windowHeight) : this("Overlay", windowWidth, windowHeight)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Overlay"/> class.
        /// </summary>
        /// <param name="windowTitle">
        /// Title of the window created by the overlay
        /// </param>
        /// <param name="windowWidth">
        /// width to use when creating the clickable  transparent overlay window
        /// </param>
        /// <param name="windowHeight">
        /// height to use when creating the clickable transparent overlay window
        /// </param>
        public Overlay(string windowTitle, int windowWidth, int windowHeight) : this(windowTitle, false, windowWidth, windowHeight)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Overlay"/> class.
        /// </summary>
        /// <param name="windowTitle">
        /// Title of the window created by the overlay
        /// </param>
        /// <param name="DPIAware">
        /// should the overlay scale with windows scale value or not.
        /// </param>
        /// <param name="vsync">
        /// vsync is enabled if true otherwise disabled.
        /// </param>
        /// <param name="windowWidth">
        /// width to use when creating the clickable  transparent overlay window
        /// </param>
        /// <param name="windowHeight">
        /// height to use when creating the clickable transparent overlay window
        /// </param>
        public Overlay(string windowTitle, bool DPIAware, int windowWidth, int windowHeight)
        {
            this.initialWindowWidth = windowWidth;
            this.initialWindowHeight = windowHeight;
            this.VSync = false;
            this.FPSLimit = 60;
            this._disposedValue = false;
            this.overlayIsReady = false;
            this.title = windowTitle;
            this.cancellationTokenSource = new();
            this.format = Format.R8G8B8A8_UNorm;
            this.loadedTexturesPtrs = new();
            this.fontUpdates = new();
            if (DPIAware)
            {
                User32.SetProcessDPIAware();
            }
        }

        #endregion

        #region PublicAPI

        /// <summary>
        /// Starts the overlay
        /// </summary>
        /// <returns>A Task that finishes once the overlay window is ready</returns>
        public async Task Start()
        {
            this.renderThread = new Thread(async () =>
            {
                await this.InitializeResources();
                this.ReplaceFontIfRequired();
                this.RunInfiniteLoop(this.cancellationTokenSource.Token);
            });

            this.renderThread.Start();
            await WaitHelpers.SpinWait(() => this.overlayIsReady);
        }

        /// <summary>
        /// Starts the overlay and waits for the overlay window to be closed.
        /// </summary>
        /// <returns>A task that finishes once the overlay window closes</returns>
        public virtual async Task Run()
        {
            if (!this.overlayIsReady)
            {
                await this.Start();
            }

            await WaitHelpers.SpinWait(() => this.cancellationTokenSource.IsCancellationRequested);
        }

        /// <summary>
        /// Safely Closes the Overlay.
        /// </summary>
        public virtual void Close()
        {
            this.cancellationTokenSource.Cancel();
        }

        /// <summary>
        /// Safely dispose all the resources created by the overlay
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Replaces the ImGui font with another one.
        /// </summary>
        /// <param name="pathName">pathname to the TTF font file.</param>
        /// <param name="size">font size to load.</param>
        /// <param name="language">supported language by the font.</param>
        /// <returns>true if the font replacement is valid otherwise false.</returns>
        public unsafe bool ReplaceFont(string pathName, int size, FontGlyphRangeType language)
        {
            if (!File.Exists(pathName))
            {
                return false;
            }

            this.fontUpdates.Enqueue(config =>
            {
                var io = ImGui.GetIO();
                var glyphRange = language switch
                {
                    FontGlyphRangeType.English => io.Fonts.GetGlyphRangesDefault(),
                    FontGlyphRangeType.ChineseSimplifiedCommon => io.Fonts.GetGlyphRangesChineseSimplifiedCommon(),
                    FontGlyphRangeType.ChineseFull => io.Fonts.GetGlyphRangesChineseFull(),
                    FontGlyphRangeType.Japanese => io.Fonts.GetGlyphRangesJapanese(),
                    FontGlyphRangeType.Korean => io.Fonts.GetGlyphRangesKorean(),
                    FontGlyphRangeType.Thai => io.Fonts.GetGlyphRangesThai(),
                    FontGlyphRangeType.Vietnamese => io.Fonts.GetGlyphRangesVietnamese(),
                    FontGlyphRangeType.Cyrillic => io.Fonts.GetGlyphRangesCyrillic(),
                    _ => throw new Exception($"Font Glyph Range (${language}) is not supported.")
                };

                io.Fonts.AddFontFromFileTTF(pathName, size, config, glyphRange);
                ImGuiNative.igGetIO()->FontDefault = null;
            });

            return true;
        }

        /// <summary>
        /// Replaces the ImGui font with another one.
        /// </summary>
        /// <param name="pathName">pathname to the TTF font file.</param>
        /// <param name="size">font size to load.</param>
        /// <param name="glyphRange">custom glyph range of the font to load. Read <see cref="FontGlyphRangeType"/> for more detail.</param>
        /// <returns>>true if the font replacement is valid otherwise false.</returns>
        public unsafe bool ReplaceFont(string pathName, int size, ushort[] glyphRange)
        {
            if (!File.Exists(pathName))
            {
                return false;
            }

            this.fontUpdates.Enqueue(config =>
            {
                var io = ImGui.GetIO();
                fixed (ushort* p = &glyphRange[0])
                {
                    io.Fonts.AddFontFromFileTTF(pathName, size, config, new IntPtr(p));
                    ImGuiNative.igGetIO()->FontDefault = null;
                }
            });

            return true;
        }

        /// <summary>
        /// Replaces the ImGui font with the default ImGui font.
        /// </summary>
        /// <returns>always return true</returns>
        public unsafe bool ReplaceFont()
        {
            this.fontUpdates.Enqueue(config =>
            {
                var io = ImGui.GetIO();
                io.Fonts.AddFontDefault(config);
                ImGuiNative.igGetIO()->FontDefault = null;
            });

            return true;
        }

        /// <summary>
        /// Replaces the ImGui font with another one.
        /// </summary>
        /// <param name="fontLoadDelegate">instructions for loading the font</param>
        public unsafe bool ReplaceFont(FontHelper.FontLoadDelegate fontLoadDelegate)
        {
            // have to do this because of issue: https://github.com/ocornut/imgui/issues/6858
            ImGuiNative.igGetIO()->FontDefault = null;
            this.fontUpdates.Enqueue(fontLoadDelegate);
            return true;
        }

        /// <summary>
        /// Enable or disable the vsync on the overlay.
        /// You can either use the <see cref="FPSLimit"/> or <see cref="VSync"/>.
        /// VSync will be given the preference if both are set.
        /// </summary>
        public bool VSync;

        /// <summary>
        /// Gets or sets the FPS Limits of the overlay.
        /// You can either use the <see cref="FPSLimit"/> or <see cref="VSync"/>.
        /// VSync will be given the preference if both are set.
        /// </summary>
        public int FPSLimit
        {
            get => this.fpslimit;
            set
            {
                if (value == 0)
                {
                    this.fpslimit = value;
                    _ = Winmm.MM_EndPeriod(1);
                }
                else if (value > 0)
                {
                    this.fpslimit = value;
                    _ = Winmm.MM_BeginPeriod(1);
                }
                else
                {
                    // ignore negative values.
                }
            }
        }

        /// <summary>
        /// Gets or sets the position of the overlay window.
        /// </summary>
        public Point Position
        {
            get
            {
                return this.window.Dimensions.Location;
            }

            set
            {
                if (this.window.Dimensions.Location != value)
                {
                    this.window.Dimensions.Location = value;
                    User32.MoveWindow(this.window.Handle, value.X, value.Y, this.window.Dimensions.Width, this.window.Dimensions.Height, true);
                }
            }
        }

        /// <summary>
        /// Gets or sets the size of the overlay window.
        /// </summary>
        public Size Size
        {
            get
            {
                return this.window.Dimensions.Size;
            }
            set
            {
                if (this.window.Dimensions.Size != value)
                {
                    this.window.Dimensions.Size = value;
                    User32.MoveWindow(this.window.Handle, this.window.Dimensions.X, this.window.Dimensions.Y, value.Width, value.Height, true);
                }
            }
        }

        /// <summary>
        /// Gets the number of displays available on the computer.
        /// </summary>
        public static int NumberVideoDisplays
        {
            get
            {
                return User32.GetSystemMetrics(0x50); // SM_CMONITORS
            }
        }

        /// <summary>
        /// Adds the image to the Graphic Device as a texture.
        /// Then returns the pointer of the added texture. It also
        /// cache the image internally rather than creating a new texture on every call,
        /// so this function can be called multiple times per frame.
        /// </summary>
        /// <param name="filePath">Path to the image on disk.</param>
        /// <param name="srgb"> a value indicating whether pixel format is srgb or not.</param>
        /// <param name="handle">output pointer to the image in the graphic device.</param>
        /// <param name="width">width of the loaded texture.</param>
        /// <param name="height">height of the loaded texture.</param>
        public void AddOrGetImagePointer(string filePath, bool srgb, out IntPtr handle, out uint width, out uint height)
        {
            if (this.loadedTexturesPtrs.TryGetValue(filePath, out var data))
            {
                handle = data.Handle;
                width = data.Width;
                height = data.Height;
            }
            else
            {
                var decorderOptions = new DecoderOptions();
                decorderOptions.Configuration.PreferContiguousImageBuffers = true;
                using var image = Image.Load<Rgba32>(decorderOptions, filePath);
                handle = this.renderer.CreateImageTexture(image, srgb ? Format.R8G8B8A8_UNorm_SRgb : Format.R8G8B8A8_UNorm);
                width = (uint)image.Width;
                height = (uint)image.Height;
                this.loadedTexturesPtrs.Add(filePath, new(handle, width, height));
            }
        }

        /// <summary>
        /// Adds the image to the Graphic Device as a texture.
        /// Then returns the pointer of the added texture. It also
        /// cache the image internally rather than creating a new texture on every call,
        /// so this function can be called multiple times per frame.
        /// </summary>
        /// <param name="name">user friendly name given to the image.</param>
        /// <param name="image">Image data in <see cref="Image"> format.</param>
        /// <param name="srgb"> a value indicating whether pixel format is srgb or not.</param>
        /// <param name="handle">output pointer to the image in the graphic device.</param>
        public void AddOrGetImagePointer(string name, Image<Rgba32> image, bool srgb, out IntPtr handle)
        {
            if (this.loadedTexturesPtrs.TryGetValue(name, out var data))
            {
                handle = data.Handle;
            }
            else
            {
                handle = this.renderer.CreateImageTexture(image, srgb ? Format.R8G8B8A8_UNorm_SRgb : Format.R8G8B8A8_UNorm);
                this.loadedTexturesPtrs.Add(name, new(handle, (uint)image.Width, (uint)image.Height));
            }
        }

        /// <summary>
        /// Removes the image from the Overlay.
        /// </summary>
        /// <param name="key">name or pathname which was used to add the image in the first place.</param>
        /// <returns> true if the image is removed otherwise false.</returns>
        public bool RemoveImage(string key)
        {
            if (this.loadedTexturesPtrs.Remove(key, out var data))
            {
                return this.renderer.RemoveImageTexture(data.Handle);
            }

            return false;
        }

        // PoEformance: the device, for drawing on it - see NOTICE.md.

        /// <summary>
        /// Gets the Direct3D 11 device the overlay draws with. Free-threaded: resources may be made on any thread.
        /// Null until the overlay has initialized.
        /// </summary>
        protected ID3D11Device Device => this.device;

        /// <summary>
        /// Gets the device's immediate context. Only for the render thread - inside <see cref="Render"/>.
        /// Whatever it is left set to, the ImGui renderer sets its own state after <see cref="Render"/> returns.
        /// </summary>
        protected ID3D11DeviceContext DeviceContext => this.deviceContext;

        /// <summary>
        /// Hands ImGui a texture the caller made and keeps: the handle draws it, as a handle from
        /// <see cref="AddOrGetImagePointer(string, Image{Rgba32}, bool, out IntPtr)"/> does. Render thread only.
        /// </summary>
        /// <param name="view">The view; it stays the caller's to release, after <see cref="DropView"/>.</param>
        /// <returns>The handle ImGui draws it by.</returns>
        protected IntPtr AddView(ID3D11ShaderResourceView view)
        {
            IntPtr handle = this.renderer.RegisterTexture(view);
            this.borrowed.Add(handle);
            return handle;
        }

        /// <summary>
        /// Takes a view handed over by <see cref="AddView"/> back from ImGui without releasing it. Render thread only.
        /// </summary>
        /// <returns>true if ImGui held it.</returns>
        protected bool DropView(IntPtr handle)
        {
            this.borrowed.Remove(handle);
            return this.renderer.DeRegisterTexture(handle) != null;
        }

        // PoEformance: the loop's health, the device's rebuild and the window put back in front - see NOTICE.md.

        /// <summary>How many frames the loop has finished - read from any thread, for a watchdog.</summary>
        protected long FramesDone => Interlocked.Read(ref this.framesDone);

        /// <summary>What the loop is doing right now, in words - read from any thread, for a watchdog.</summary>
        protected string Stage => this.stage;

        /// <summary>
        /// Told one line whenever something about the window or the device changes - Present's answer,
        /// a rebuild, a display change. Called on the render thread. Nothing by default.
        /// </summary>
        protected virtual void Report(string line)
        {
        }

        /// <summary>
        /// Called on the render thread once the device was made anew: every texture handle from the old
        /// one is gone - the images' and every view lent by <see cref="AddView"/> - and has to be made again.
        /// </summary>
        protected virtual void DeviceRebuilt()
        {
        }

        /// <summary>
        /// Puts the window at the top of the always-on-top windows and shows it, without taking the focus -
        /// once this frame is presented. Render thread.
        /// </summary>
        protected void KeepOnTop() => this.onTopAsked = true;

        /// <summary>
        /// Everything that puts the overlay back on the screen, whatever went wrong - once this frame is
        /// presented: in front, shown, its glass and click-through styles laid on again, and the device,
        /// swap chain and every texture made anew. Render thread.
        /// </summary>
        /// <remarks>
        /// BETWEEN FRAMES AND NOT WHEN ASKED: ImGui's font atlas is locked from NewFrame to Render, and a
        /// rebuild makes it again; the frame being built also names the old textures.
        /// </remarks>
        protected void Restore(string why) => this.restoreAsked = why;

        private volatile bool onTopAsked;
        private volatile string restoreAsked;

        /// <summary>Does what was asked during the frame, now that it is presented.</summary>
        private void BetweenFrames()
        {
            if (this.onTopAsked || this.restoreAsked != null)
            {
                this.onTopAsked = false;
                User32.SetWindowPos(this.window.Handle, User32.TopMost, 0, 0, 0, 0, User32.OnTopFlags);
            }

            if (this.restoreAsked is { } why)
            {
                this.restoreAsked = null;

                // Where the overlay believes it is, whatever moved the real window since: Position and
                // Size only move it when the values they are handed differ from these.
                var at = this.window.Dimensions;
                User32.MoveWindow(this.window.Handle, at.X, at.Y, at.Width, at.Height, true);
                Utils.ReapplyTransparency(this.window.Handle);
                this.RebuildDevice(why);
                this.Report($"restored: {this.WindowState()}");
            }
        }

        /// <summary>
        /// The window and the device as they stand, in one line: shown or hidden, on top or not, where, and
        /// what Present last answered. Any thread.
        /// </summary>
        protected string WindowState()
        {
            IntPtr handle = this.window?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero)
            {
                return "no overlay window yet";
            }

            var styles = (WindowExStyles)User32.GetWindowLong(handle, (int)WindowLongParam.GWL_EXSTYLE);
            User32.GetWindowRect(handle, out WindowRect rect);
            return $"overlay window {(User32.IsWindowVisible(handle) ? "shown" : "HIDDEN")}, "
                + $"{((styles & WindowExStyles.WS_EX_TOPMOST) != 0 ? "on top" : "NOT ON TOP")}, "
                + $"{((styles & WindowExStyles.WS_EX_TRANSPARENT) != 0 ? "click-through" : "clickable")}, "
                + $"at ({rect.Left}, {rect.Top}) {rect.Right - rect.Left}x{rect.Bottom - rect.Top}; "
                + $"present {Named(this.presentCode)}{this.removal}; {this.FramesDone} frames, {this.rebuilds} rebuilds";
        }

        #endregion

        protected virtual void Dispose(bool disposing)
        {
            if (this._disposedValue)
            {
                return;
            }

            if (disposing)
            {
                if (this.FPSLimit > 0)
                {
                    Winmm.MM_EndPeriod(1);
                }

                this.renderThread?.Join();
                foreach(var key in this.loadedTexturesPtrs.Keys.ToArray())
                {
                    this.RemoveImage(key);
                }

                this.cancellationTokenSource?.Dispose();
                this.fontUpdates?.Clear();
                this.swapChain?.Release();
                this.backBuffer?.Release();
                this.renderView?.Release();
                this.renderer?.Dispose();
                this.window?.Dispose();
                this.deviceContext?.Release();
                this.device?.Release();
            }

            if (this.selfPointer != IntPtr.Zero)
            {
                if (!User32.UnregisterClass(this.title, this.selfPointer))
                {
                    throw new Exception($"Failed to Unregister {this.title} class during dispose.");
                }

                this.selfPointer = IntPtr.Zero;
            }

            this._disposedValue = true;
        }

        /// <summary>
        /// Steps to execute after the overlay has fully initialized.
        /// </summary>
        protected virtual Task PostInitialized()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Abstract Task for creating the UI.
        /// </summary>
        /// <returns>Task that finishes once per frame</returns>
        protected abstract void Render();

        private void RunInfiniteLoop(CancellationToken token)
        {
            var stopwatch = Stopwatch.StartNew();
            var currentTimeSec = 0f;
            var clearColor = new Color4(0.0f);
            var delayMs = 0f;
            var sleepTimeMs = 0;
            while (!token.IsCancellationRequested)
            {
                currentTimeSec = stopwatch.ElapsedTicks / (float)Stopwatch.Frequency;
                stopwatch.Restart();
                this.stage = "pumping messages";
                this.window.PumpEvents();

                // PoEformance: a rebuild that could not finish leaves no swap chain; asked again each second
                // until one does, rather than drawing on nothing. See NOTICE.md.
                if (this.swapChain == null || this.renderView == null)
                {
                    this.stage = "waiting for a graphics device";
                    this.BetweenFrames();
                    this.TryRebuild("the last attempt left no swap chain");
                    Thread.Sleep(100);
                    continue;
                }

                this.stage = "reading input";
                Utils.SetOverlayClickable(this.window.Handle, this.inputhandler.Update());
                this.stage = "building the frame";
                this.renderer.Update(currentTimeSec, () => { Render(); });
                this.stage = "drawing";
                this.deviceContext.OMSetRenderTargets(renderView);
                this.deviceContext.ClearRenderTargetView(renderView, clearColor);
                this.renderer.Render();
                this.stage = "presenting";

                // PoEformance: Present's answer is read, not dropped - see Presented.
                SharpGen.Runtime.Result presented;
                if (VSync)
                {
                    presented = this.swapChain.Present(1, PresentFlags.None); // Present with vsync
                }
                else if (this.FPSLimit > 0)
                {
                    presented = this.swapChain.Present(0, PresentFlags.None);
                    delayMs = 1000f / this.FPSLimit;
                    currentTimeSec = stopwatch.ElapsedTicks / (float)Stopwatch.Frequency;
                    sleepTimeMs = (int)(delayMs - (currentTimeSec * 1000));
                    if (sleepTimeMs > 0)
                    {
                        this.stage = "sleeping";
                        Thread.Sleep(sleepTimeMs);
                    }
                }
                else
                {
                    presented = this.swapChain.Present(0, PresentFlags.None); // Present without vsync
                }

                this.Presented(presented);
                this.stage = "putting the window back";
                this.BetweenFrames();
                this.stage = "loading fonts";
                this.ReplaceFontIfRequired();
                Interlocked.Increment(ref this.framesDone);
            }
        }

        // PoEformance: what Present answers, which upstream dropped - see NOTICE.md.
        //
        // A LOST DEVICE IS SILENT OTHERWISE. Present says DXGI_ERROR_DEVICE_REMOVED and every frame after
        // it draws into nothing, while the rest of the loop carries on as if all were well - a mapped
        // vertex buffer with write-discard still hands back memory on a removed device - so the overlay
        // stays empty until the process is ended. The answer is written down once per change, and a
        // lost device is made again.
        //
        // OCCLUDED IS A SLEEP. With nothing of the window on show Present returns at once instead of
        // waiting for the screen, and with VSync on nothing else in the loop waits: it would run flat out
        // drawing frames nobody sees.
        private void Presented(SharpGen.Runtime.Result presented)
        {
            int code = presented.Code;
            if (code != this.presentCode)
            {
                string removed = this.Removal();
                this.Report(code == 0
                    ? $"present: fine again after {Named(this.presentCode)}"
                    : $"present: {Named(code)}{removed}");
                this.presentCode = code;
            }

            if (code == Occluded)
            {
                this.stage = "sleeping, nothing on show";
                Thread.Sleep(16);
            }
            else if (IsLost(code))
            {
                this.TryRebuild($"present said {Named(code)}");
            }
        }

        private static bool IsLost(int code)
            => code == Vortice.DXGI.ResultCode.DeviceRemoved.Code || code == Vortice.DXGI.ResultCode.DeviceReset.Code || code == Vortice.DXGI.ResultCode.DeviceHung.Code;

        private static string Named(int code)
        {
            if (code == 0)
            {
                return "S_OK";
            }

            if (code == Occluded)
            {
                return "DXGI_STATUS_OCCLUDED";
            }

            if (code == Vortice.DXGI.ResultCode.DeviceRemoved.Code)
            {
                return "DXGI_ERROR_DEVICE_REMOVED";
            }

            if (code == Vortice.DXGI.ResultCode.DeviceReset.Code)
            {
                return "DXGI_ERROR_DEVICE_RESET";
            }

            if (code == Vortice.DXGI.ResultCode.DeviceHung.Code)
            {
                return "DXGI_ERROR_DEVICE_HUNG";
            }

            return $"0x{code:X8}";
        }

        /// <summary>
        /// Why the device was removed, where it was - the driver's own reason, which Present does not give.
        /// Render thread only: a watchdog asking the device while a rebuild lets it go would ask a released
        /// object, so it reads what this kept instead.
        /// </summary>
        private string Removal()
        {
            int reason = this.device?.DeviceRemovedReason.Code ?? 0;
            this.removal = reason == 0 ? string.Empty : $" (the device was removed: {Named(reason)})";
            return this.removal;
        }

        /// <summary>A rebuild at most once a second, so a driver that refuses every attempt is not asked every frame.</summary>
        private void TryRebuild(string why)
        {
            long now = Environment.TickCount64;
            if (now < this.nextRebuild)
            {
                return;
            }

            this.nextRebuild = now + 1000;
            this.RebuildDevice(why);
        }

        /// <summary>
        /// Makes the device, the swap chain, ImGui's own textures and every image anew, ImGui's context
        /// kept. Whatever was made on the old device is let go first: a removed device's objects can be
        /// released, and nothing else can be done with them.
        /// </summary>
        private void RebuildDevice(string why)
        {
            this.Report($"rebuilding the graphics device: {why}{this.Removal()}");
            string was = this.stage;
            this.stage = "rebuilding the graphics device";
            try
            {
                this.ReleaseSwapChain();
                this.loadedTexturesPtrs.Clear();

                ID3D11Device oldDevice = this.device;
                ID3D11DeviceContext oldContext = this.deviceContext;
                oldContext?.ClearState();
                oldContext?.Flush();

                this.CreateDevice();
                this.renderer.Rebind(this.device, this.deviceContext, this.borrowed.Contains);
                this.borrowed.Clear();
                oldContext?.Release();
                oldDevice?.Release();

                this.CreateSwapChain(this.window.Dimensions.Width, this.window.Dimensions.Height);
                this.renderer.Resize(this.window.Dimensions.Width, this.window.Dimensions.Height);
                this.rebuilds++;
                this.Removal();
                this.DeviceRebuilt();
                this.Report($"rebuilt the graphics device at feature level {this.device.FeatureLevel} ({this.rebuilds} this session)");
            }
            catch (Exception exception)
            {
                this.Report($"the rebuild failed, tried again in a second: {exception.GetType().Name}: {exception.Message}");
            }
            finally
            {
                this.stage = was;
            }
        }

        private void ReleaseSwapChain()
        {
            this.renderView?.Dispose();
            this.renderView = null;
            this.backBuffer?.Dispose();
            this.backBuffer = null;
            this.swapChain?.Dispose();
            this.swapChain = null;
        }

        private void CreateSwapChain(int width, int height)
        {
            // Each step let go of, since a rebuild may run this once a second while a driver refuses.
            using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetParent<IDXGIAdapter>();
            using var dxgiFactory = adapter.GetParent<IDXGIFactory>();
            var swapchainDesc = new SwapChainDescription()
            {
                BufferCount = 1,
                BufferDescription = new ModeDescription(width, height, this.format),
                Windowed = true,
                OutputWindow = this.window.Handle,
                SampleDescription = new SampleDescription(1, 0),
                SwapEffect = SwapEffect.Discard,
                BufferUsage = Usage.RenderTargetOutput,
            };

            this.swapChain = dxgiFactory.CreateSwapChain(this.device, swapchainDesc);
            dxgiFactory.MakeWindowAssociation(this.window.Handle, WindowAssociationFlags.IgnoreAll);

            this.backBuffer = this.swapChain.GetBuffer<ID3D11Texture2D>(0);
            this.renderView = this.device.CreateRenderTargetView(backBuffer);
        }

        // PoEformance: the device made at the newest of the feature levels the card offers - see InitializeResources and NOTICE.md.
        private void CreateDevice()
        {
            if (D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.None,
                new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 },
                out this.device,
                out this.deviceContext).Failure)
            {
                D3D11.D3D11CreateDevice(
                    null,
                    DriverType.Hardware,
                    DeviceCreationFlags.None,
                    new[] { FeatureLevel.Level_10_0 },
                    out this.device,
                    out this.deviceContext).CheckError();
            }
        }

        private void ReplaceFontIfRequired()
        {
            if (this.renderer != null)
            {
                while (this.fontUpdates.TryDequeue(out var update))
                {
                    this.renderer.UpdateFontTexture(update);
                }
            }
        }

        private void OnResize(int width, int height)
        {
            // PoEformance: a resize on a lost device throws, and here - inside the message pump - that
            // would end the process. The swap chain is let go instead, and the loop makes the device
            // anew. See NOTICE.md.
            try
            {
                if (this.swapChain == null || this.renderView == null)//first show, or after a failed rebuild
                {
                    this.ReleaseSwapChain();
                    this.CreateSwapChain(width, height);
                }
                else
                {
                    this.renderView.Dispose();
                    this.backBuffer.Dispose();

                    this.swapChain.ResizeBuffers(1, width, height, this.format, SwapChainFlags.None);

                    backBuffer = this.swapChain.GetBuffer<ID3D11Texture2D1>(0);
                    renderView = this.device.CreateRenderTargetView(backBuffer);
                }
            }
            catch (SharpGen.Runtime.SharpGenException exception)
            {
                this.Report($"resizing to {width}x{height} failed, the device is made anew: {exception.Message}{this.Removal()}");
                this.ReleaseSwapChain();
            }

            this.renderer.Resize(width, height);
        }

        private async Task InitializeResources()
        {
            // PoEformance: the newest feature level the card has rather than 10.0 alone - shader model 5
            // and BC6H/BC7 textures need 11.0 - and 10.0 still where nothing newer is offered. See NOTICE.md.
            this.CreateDevice();
            this.selfPointer = Kernel32.GetModuleHandle(null);
            this.wndClass = new WNDCLASSEX
            {
                Size = Unsafe.SizeOf<WNDCLASSEX>(),
                Styles = WindowClassStyles.CS_HREDRAW | WindowClassStyles.CS_VREDRAW | WindowClassStyles.CS_PARENTDC,
                WindowProc = WndProc,
                InstanceHandle = this.selfPointer,
                CursorHandle = User32.LoadCursor(IntPtr.Zero, SystemCursor.IDC_ARROW),
                BackgroundBrushHandle = IntPtr.Zero,
                IconHandle = IntPtr.Zero,
                MenuName = string.Empty,
                ClassName = this.title,
                SmallIconHandle= IntPtr.Zero,
                ClassExtraBytes = 0,
                WindowExtraBytes = 0
            };

            if (User32.RegisterClassEx(ref this.wndClass) == 0)
            {
                throw new Exception($"Failed to Register class of name {this.wndClass.ClassName}");
            }

            this.window = new Win32Window(
                wndClass.ClassName,
                this.initialWindowWidth,
                this.initialWindowHeight,
                0,
                0,
                this.title,
                WindowStyles.WS_POPUP,
                // PoEformance: TOOLWINDOW keeps the overlay out of the taskbar and Alt+Tab; the exe has no
                // console any more, so this popup would otherwise be the one entry shown.
                WindowExStyles.WS_EX_ACCEPTFILES | WindowExStyles.WS_EX_TOPMOST | WindowExStyles.WS_EX_TOOLWINDOW);
            this.renderer = new ImGuiRenderer(device, deviceContext, this.initialWindowWidth, this.initialWindowHeight);
            this.inputhandler = new ImGuiInputHandler(this.window.Handle);
            this.overlayIsReady = true;
            await this.PostInitialized();
            User32.ShowWindow(this.window.Handle, ShowWindowCommand.Show);
            Utils.InitTransparency(this.window.Handle);
        }

        private bool ProcessMessage(WindowMessage msg, UIntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case WindowMessage.ShowWindow:
                    this.OnResize(this.window.Dimensions.Width, this.window.Dimensions.Height);
                    break;
                case WindowMessage.Size:
                    switch ((SizeMessage)wParam)
                    {
                        case SizeMessage.SIZE_RESTORED:
                        case SizeMessage.SIZE_MAXIMIZED:
                            var lp = (int)lParam;
                            this.OnResize(Utils.Loword(lp), Utils.Hiword(lp));
                            break;
                        default:
                            break;
                    }

                    break;
                case WindowMessage.Destroy:
                    this.Close();
                    break;

                // PoEformance: a display mode change or the compositor restarting, written down beside
                // what Present says about it - see NOTICE.md.
                case (WindowMessage)0x007E: // WM_DISPLAYCHANGE
                    this.Report($"the display changed: {Utils.Loword((int)lParam)}x{Utils.Hiword((int)lParam)} at {(uint)wParam} bits a pixel");
                    break;
                case (WindowMessage)0x031E: // WM_DWMCOMPOSITIONCHANGED
                    this.Report("the desktop compositor changed");
                    break;
                default:
                    break;
            }

            return false;
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam)
        {
            if (this.overlayIsReady)
            {
                if (this.inputhandler.ProcessMessage((WindowMessage)msg, wParam, lParam) ||
                    this.ProcessMessage((WindowMessage)msg, wParam, lParam))
                {
                    return IntPtr.Zero;
                }
            }

            return User32.DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }
}
