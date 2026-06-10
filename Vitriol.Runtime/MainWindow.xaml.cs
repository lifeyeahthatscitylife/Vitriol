using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using Vitriol.Engine.Maps;
using Vitriol.Shared.Maps;
using IOPath = System.IO.Path;
using System.Windows.Interop;

namespace Vitriol.Runtime;

public partial class MainWindow : Window
{
    // ------------------------------------------------------------
    // Fullscreen lock
    // ------------------------------------------------------------
    private bool _fullscreenApplying = false;

    private void ForceFullscreen()
    {
        if (_fullscreenApplying) return;
        _fullscreenApplying = true;

        try
        {
            if (WindowStyle != WindowStyle.None) WindowStyle = WindowStyle.None;
            if (ResizeMode != ResizeMode.NoResize) ResizeMode = ResizeMode.NoResize;
            if (WindowState != WindowState.Maximized) WindowState = WindowState.Maximized;
        }
        finally
        {
            _fullscreenApplying = false;
        }
    }

    private void HookFullscreenGuards()
    {
        Loaded += (_, _) => ForceFullscreen();
        StateChanged += (_, _) => ForceFullscreen();
        SizeChanged += (_, _) => ForceFullscreen();
        Activated += (_, _) => ForceFullscreen();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        ForceFullscreen();

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            const int GWL_STYLE = -16;
            const int WS_SYSMENU = 0x00080000;

            int style = NativeMethods.GetWindowLong(hwnd, GWL_STYLE);
            style &= ~WS_SYSMENU;
            NativeMethods.SetWindowLong(hwnd, GWL_STYLE, style);
        }
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }

    // ------------------------------------------------------------
    // Project folders
    // ------------------------------------------------------------
    private readonly string _projectRoot;
    private string MapsFolder => IOPath.Combine(_projectRoot, "maps");
    private string PngsFolder => IOPath.Combine(_projectRoot, "assets", "tilesets");

    private MapDto? _map;
    private string? _mapPath;

    private const double DeadzoneFracX = 0.55;
    private const double DeadzoneFracY = 0.28;

    private int _playerX = 0;
    private int _playerY = 0;
    private readonly Dictionary<string, string> _flags = new(StringComparer.OrdinalIgnoreCase);

    // ------------------------------------------------------------
    // Tileset caches + compiled runtime layout (multi-tileset, global indices)
    // ------------------------------------------------------------
    private readonly ConcurrentDictionary<string, BitmapSource> _tilesetBitmapCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, CroppedBitmap>> _tileCropCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _tilesetCompileLock = new();
    private List<TilesetRuntime> _compiledTilesets = new();
    private bool _tilesetsDirty = true;

    private sealed record TilesetMeta(
        int PixelWidth,
        int PixelHeight,
        int TileWidth,
        int TileHeight,
        int Margin,
        int Spacing,
        int TilesAcross,
        int TilesDown,
        int TileCount
    );

    private sealed class TilesetRuntime
    {
        public required TilesetRefDto Ref { get; init; }
        public required string FullPath { get; init; }
        public required int StartIndexGlobal { get; init; }
        public required int TileCount { get; init; }
        public required int TilesAcross { get; init; }
        public required int TileW { get; init; }
        public required int TileH { get; init; }
        public required int Margin { get; init; }
        public required int Spacing { get; init; }
        public required BitmapSource Bitmap { get; init; }
        public required ConcurrentDictionary<int, CroppedBitmap> CropCache { get; init; }
    }

    // ------------------------------------------------------------
    // Viewport-only renderer
    // ------------------------------------------------------------
    private Image? _viewportImage;
    private readonly DispatcherTimer _renderDebounce;

    public MainWindow()
    {
        InitializeComponent();

        _projectRoot = ResolveProjectRoot();

        HookFullscreenGuards();

        Loaded += (_, _) =>
        {
            ForceFullscreen();
            Boot();
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(FocusGameSurface));
        };

        _renderDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _renderDebounce.Tick += (_, _) =>
        {
            _renderDebounce.Stop();
            RenderViewport();
        };
    }

    private void FocusGameSurface()
    {
        Keyboard.Focus(ViewCanvas);
        ViewCanvas.Focus();
    }

    // ------------------------------------------------------------
    // Project root resolution
    // ------------------------------------------------------------
    private static string ResolveProjectRoot()
    {
        var start = AppContext.BaseDirectory;

        var candidates = new[]
        {
            start,
            Directory.GetCurrentDirectory()
        }
        .Where(p => !string.IsNullOrWhiteSpace(p))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        const int maxUp = 8;

        foreach (var baseDir in candidates)
        {
            try
            {
                var dir = new DirectoryInfo(baseDir);
                for (int i = 0; i < maxUp && dir is not null; i++)
                {
                    var probe = IOPath.Combine(dir.FullName, "Vitriol.Projects");
                    if (Directory.Exists(probe))
                        return probe;

                    dir = dir.Parent;
                }
            }
            catch { }
        }

        return IOPath.Combine(Directory.GetCurrentDirectory(), "Vitriol.Projects");
    }

    private void Boot()
    {
        HudText.Text = $"Runtime ready. Click 'Load Map'.  (Project: {_projectRoot})";
    }

    // ------------------------------------------------------------
    // UI: Load Map button
    // ------------------------------------------------------------
    private void OnLoadMapClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Directory.Exists(MapsFolder))
            {
                HudText.Text = $"Maps folder not found: {MapsFolder}";
                FocusGameSurface();
                return;
            }

            var dlg = new OpenFileDialog
            {
                Title = "Open Vitriol Map",
                Filter = "Vitriol Map (*.vitmap.json)|*.vitmap.json|JSON (*.json)|*.json|All files (*.*)|*.*",
                InitialDirectory = MapsFolder,
                CheckFileExists = true,
                Multiselect = false
            };

            if (dlg.ShowDialog(this) != true)
            {
                FocusGameSurface();
                return;
            }

            _playerX = 0;
            _playerY = 0;

            LoadMap(dlg.FileName, "Manual Load");
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(FocusGameSurface));
        }
        catch (Exception ex)
        {
            HudText.Text = $"Load map dialog failed: {ex.Message}";
            FocusGameSurface();
        }
    }

    // ------------------------------------------------------------
    // Map loading
    // ------------------------------------------------------------
    private void LoadMap(string path, string reason)
    {
        try
        {
            _mapPath = path;
            _map = MapSerializer.LoadFromFile(path);

            if (_map.Events is null)
                _map = _map with { Events = Array.Empty<MapEventDto>() };

            EnsureCollisionLayer();

            var errors = ValidateTilesetFilesExist(_map);
            if (errors.Count > 0)
            {
                _map = null;
                HudText.Text = "Map load blocked:\n" + string.Join("\n", errors);
                return;
            }

            MarkTilesetsDirty();
            EnsureTilesetsCompiled();

            _playerX = Math.Clamp(_playerX, 0, _map.Width - 1);
            _playerY = Math.Clamp(_playerY, 0, _map.Height - 1);

            EnsureViewportImageExists();

            ViewCanvas.Width = checked(_map.Width * _map.TileSize);
            ViewCanvas.Height = checked(_map.Height * _map.TileSize);

            HudText.Text = $"{reason}: Loaded {IOPath.GetFileName(path)}  Player({_playerX},{_playerY})";

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                CenterCameraOnPlayer();
                RequestRender();
                FocusGameSurface();
            }));
        }
        catch (Exception ex)
        {
            HudText.Text = $"Load failed: {ex.Message}";
            FocusGameSurface();
        }
    }

    private List<string> ValidateTilesetFilesExist(MapDto map)
    {
        var issues = new List<string>();

        var tilesets = map.Tilesets ?? Array.Empty<TilesetRefDto>();
        foreach (var ts in tilesets)
        {
            var img = (ts.ImagePath ?? "").Trim();

            if (img.Length == 0)
                continue;

            var full = ResolveTilesetPathFromImagePath(img);
            if (full is null)
            {
                issues.Add($"Tileset '{ts.TilesetId}': ImagePath invalid: '{img}'.");
                continue;
            }

            if (!File.Exists(full))
                issues.Add($"Tileset '{ts.TilesetId}': missing PNG: '{full}'.");
        }

        return issues;
    }

    private void EnsureCollisionLayer()
    {
        if (_map is null) return;

        bool hasCollision =
            _map.Layers.Any(l => l.Kind == LayerKind.Collision || l.LayerId == "col");

        if (hasCollision) return;

        var col = new TileLayerDto(
            LayerId: "col",
            Name: "Collision",
            Kind: LayerKind.Collision,
            Visible: true,
            Tiles: new int[_map.Width * _map.Height]
        );

        _map = _map with { Layers = _map.Layers.Concat(new[] { col }).ToArray() };
    }

    private string? ResolveTilesetPathFromImagePath(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return null;

        imagePath = imagePath.Trim();

        if (IOPath.IsPathRooted(imagePath))
            return imagePath;

        var rel = imagePath.Replace('/', '\\');
        return IOPath.Combine(_projectRoot, rel);
    }

    private TileLayerDto? GetBackgroundLayer() =>
        _map?.Layers.FirstOrDefault(l => l.Kind == LayerKind.Background);

    private TileLayerDto? GetCollisionLayer() =>
        _map?.Layers.FirstOrDefault(l => l.Kind == LayerKind.Collision || l.LayerId == "col");

    private string? ResolveTilesetFullPath(TilesetRefDto ts)
    {
        var raw = (ts.ImagePath ?? "").Trim();
        if (raw.Length > 0 && IOPath.IsPathRooted(raw) && File.Exists(raw))
            return raw;

        if (raw.Length > 0)
        {
            var fileName = IOPath.GetFileName(raw.Replace('/', '\\'));
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                var inTilesets = IOPath.Combine(PngsFolder, fileName);
                if (File.Exists(inTilesets))
                    return inTilesets;

                if (IOPath.GetExtension(fileName).Length == 0)
                {
                    var inTilesetsPng = IOPath.Combine(PngsFolder, fileName + ".png");
                    if (File.Exists(inTilesetsPng))
                        return inTilesetsPng;
                }
            }
        }

        var fallbackName = (ts.TilesetId ?? "").Trim();
        if (fallbackName.Length > 0)
        {
            var p = IOPath.Combine(PngsFolder, fallbackName + ".png");
            if (File.Exists(p))
                return p;
        }

        return null;
    }

    private void MarkTilesetsDirty()
    {
        lock (_tilesetCompileLock)
        {
            _tilesetsDirty = true;
        }
    }

    private void EnsureTilesetsCompiled()
    {
        var map = _map;

        lock (_tilesetCompileLock)
        {
            if (!_tilesetsDirty) return;

            if (map is null)
            {
                _compiledTilesets = new List<TilesetRuntime>();
                _tilesetsDirty = false;
                return;
            }

            var compiled = new List<TilesetRuntime>();
            int runningStart = 0;

            var tilesets = map.Tilesets ?? Array.Empty<TilesetRefDto>();
            foreach (var ts in tilesets)
            {
                var img = (ts.ImagePath ?? "").Trim();

                if (img.Length == 0)
                    continue;

                var fullPath = ResolveTilesetPathFromImagePath(img);
                if (fullPath is null || !File.Exists(fullPath))
                    continue;

                var bmp = _tilesetBitmapCache.GetOrAdd(fullPath, LoadBitmap);
                var meta = ComputeMeta(ts, bmp);

                var cropCache = _tileCropCache.GetOrAdd(fullPath, _ => new ConcurrentDictionary<int, CroppedBitmap>());

                compiled.Add(new TilesetRuntime
                {
                    Ref = ts,
                    FullPath = fullPath,
                    StartIndexGlobal = runningStart,
                    TileCount = meta.TileCount,
                    TilesAcross = meta.TilesAcross,
                    TileW = meta.TileWidth,
                    TileH = meta.TileHeight,
                    Margin = meta.Margin,
                    Spacing = meta.Spacing,
                    Bitmap = bmp,
                    CropCache = cropCache
                });

                runningStart += meta.TileCount;
            }

            _compiledTilesets = compiled;
            _tilesetsDirty = false;
        }
    }

    private static TilesetMeta ComputeMeta(TilesetRefDto ts, BitmapSource bmp)
    {
        int tileW = Math.Max(1, ts.TileWidth);
        int tileH = Math.Max(1, ts.TileHeight);
        int margin = Math.Max(0, ts.Margin);
        int spacing = Math.Max(0, ts.Spacing);

        int stepX = tileW + spacing;
        int stepY = tileH + spacing;

        int tilesAcross = stepX > 0 ? (bmp.PixelWidth - margin) / stepX : 0;
        int tilesDown = stepY > 0 ? (bmp.PixelHeight - margin) / stepY : 0;

        tilesAcross = Math.Max(0, tilesAcross);
        tilesDown = Math.Max(0, tilesDown);

        int tileCount = tilesAcross * tilesDown;

        return new TilesetMeta(
            PixelWidth: bmp.PixelWidth,
            PixelHeight: bmp.PixelHeight,
            TileWidth: tileW,
            TileHeight: tileH,
            Margin: margin,
            Spacing: spacing,
            TilesAcross: tilesAcross,
            TilesDown: tilesDown,
            TileCount: tileCount
        );
    }

    private TilesetRuntime? FindRuntimeForGlobalTile(int globalTileIndex)
    {
        EnsureTilesetsCompiled();
        if (globalTileIndex < 0) return null;

        foreach (var ts in _compiledTilesets)
        {
            if (globalTileIndex >= ts.StartIndexGlobal &&
                globalTileIndex < ts.StartIndexGlobal + ts.TileCount)
                return ts;
        }

        return null;
    }

    private static int GetLocalIndex(TilesetRuntime ts, int globalTileIndex) =>
        globalTileIndex - ts.StartIndexGlobal;

    // ------------------------------------------------------------
    // Camera
    // ------------------------------------------------------------
    private void CenterCameraOnPlayer()
    {
        var map = _map;
        if (map is null) return;

        if (ViewScroll.ViewportWidth <= 0 || ViewScroll.ViewportHeight <= 0)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(CenterCameraOnPlayer));
            return;
        }

        double mapPxW = map.Width * map.TileSize;
        double mapPxH = map.Height * map.TileSize;

        double playerCenterX = _playerX * map.TileSize + map.TileSize / 2.0;
        double playerCenterY = _playerY * map.TileSize + map.TileSize / 2.0;

        double desiredX = playerCenterX - ViewScroll.ViewportWidth / 2.0;
        double desiredY = playerCenterY - ViewScroll.ViewportHeight / 2.0;

        double maxX = Math.Max(0, mapPxW - ViewScroll.ViewportWidth);
        double maxY = Math.Max(0, mapPxH - ViewScroll.ViewportHeight);

        desiredX = Math.Clamp(desiredX, 0, maxX);
        desiredY = Math.Clamp(desiredY, 0, maxY);

        ViewScroll.ScrollToHorizontalOffset(desiredX);
        ViewScroll.ScrollToVerticalOffset(desiredY);
    }

    private void DeadzoneFollowPlayer()
    {
        var map = _map;
        if (map is null) return;
        if (ViewScroll.ViewportWidth <= 0 || ViewScroll.ViewportHeight <= 0) return;

        double mapPxW = map.Width * map.TileSize;
        double mapPxH = map.Height * map.TileSize;

        double camX = ViewScroll.HorizontalOffset;
        double camY = ViewScroll.VerticalOffset;

        double vw = ViewScroll.ViewportWidth;
        double vh = ViewScroll.ViewportHeight;

        double dzW = vw * DeadzoneFracX;
        double dzH = vh * DeadzoneFracY;

        double dzLeft = camX + (vw - dzW) / 2.0;
        double dzTop = camY + (vh - dzH) / 2.0;
        double dzRight = dzLeft + dzW;
        double dzBottom = dzTop + dzH;

        double px = _playerX * map.TileSize + map.TileSize / 2.0;
        double py = _playerY * map.TileSize + map.TileSize / 2.0;

        if (px < dzLeft) camX -= (dzLeft - px);
        else if (px > dzRight) camX += (px - dzRight);

        if (py < dzTop) camY -= (dzTop - py);
        else if (py > dzBottom) camY += (py - dzBottom);

        double maxX = Math.Max(0, mapPxW - vw);
        double maxY = Math.Max(0, mapPxH - vh);

        camX = Math.Clamp(camX, 0, maxX);
        camY = Math.Clamp(camY, 0, maxY);

        ViewScroll.ScrollToHorizontalOffset(camX);
        ViewScroll.ScrollToVerticalOffset(camY);
    }

    // ------------------------------------------------------------
    // Rendering
    // ------------------------------------------------------------
    private void EnsureViewportImageExists()
    {
        if (_viewportImage != null) return;

        _viewportImage = new Image
        {
            Stretch = Stretch.None,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(_viewportImage, BitmapScalingMode.NearestNeighbor);

        ViewCanvas.Children.Clear();
        ViewCanvas.Children.Add(_viewportImage);
    }

    private void RequestRender()
    {
        if (_map is null) return;
        _renderDebounce.Stop();
        _renderDebounce.Start();
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_map is null) return;
        RequestRender();
    }

    private void RenderViewport()
    {
        var map = _map;
        if (map is null) return;

        EnsureViewportImageExists();
        EnsureTilesetsCompiled();

        var bg = GetBackgroundLayer();
        if (bg is null) { HudText.Text = "Map has no background layer."; return; }

        int mapPxW = checked(map.Width * map.TileSize);
        int mapPxH = checked(map.Height * map.TileSize);

        double viewLeft = ViewScroll.HorizontalOffset;
        double viewTop = ViewScroll.VerticalOffset;
        double viewW = ViewScroll.ViewportWidth;
        double viewH = ViewScroll.ViewportHeight;

        int pad = map.TileSize * 2;
        int regionX = ClampToInt(viewLeft) - pad;
        int regionY = ClampToInt(viewTop) - pad;
        int regionW = ClampToInt(viewW) + pad * 2;
        int regionH = ClampToInt(viewH) + pad * 2;

        if (regionX < 0) regionX = 0;
        if (regionY < 0) regionY = 0;
        if (regionX + regionW > mapPxW) regionW = mapPxW - regionX;
        if (regionY + regionH > mapPxH) regionH = mapPxH - regionY;

        if (regionW <= 1 || regionH <= 1) return;

        int startTileX = regionX / map.TileSize;
        int startTileY = regionY / map.TileSize;
        int endTileX = (regionX + regionW + map.TileSize - 1) / map.TileSize;
        int endTileY = (regionY + regionH + map.TileSize - 1) / map.TileSize;

        startTileX = Math.Max(0, startTileX);
        startTileY = Math.Max(0, startTileY);
        endTileX = Math.Min(map.Width, endTileX);
        endTileY = Math.Min(map.Height, endTileY);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, regionW, regionH));

            for (int y = startTileY; y < endTileY; y++)
            for (int x = startTileX; x < endTileX; x++)
            {
                int idx = y * map.Width + x;
                if ((uint)idx >= (uint)bg.Tiles.Length) continue;

                int t = bg.Tiles[idx];
                if (t < 0) continue;

                var ts = FindRuntimeForGlobalTile(t);
                if (ts is null || ts.TileCount <= 0) continue;

                int local = GetLocalIndex(ts, t);
                if ((uint)local >= (uint)ts.TileCount) continue;

                var crop = ts.CropCache.GetOrAdd(local, tileIndex =>
                {
                    int cx = ts.TilesAcross > 0 ? (tileIndex % ts.TilesAcross) : 0;
                    int cy = ts.TilesAcross > 0 ? (tileIndex / ts.TilesAcross) : 0;

                    int px = ts.Margin + cx * (ts.TileW + ts.Spacing);
                    int py = ts.Margin + cy * (ts.TileH + ts.Spacing);

                    if (px + ts.TileW > ts.Bitmap.PixelWidth || py + ts.TileH > ts.Bitmap.PixelHeight)
                    {
                        var dummy = new CroppedBitmap(ts.Bitmap, new Int32Rect(0, 0, 1, 1));
                        dummy.Freeze();
                        return dummy;
                    }

                    var cb = new CroppedBitmap(ts.Bitmap, new Int32Rect(px, py, ts.TileW, ts.TileH));
                    cb.Freeze();
                    return cb;
                });

                double drawX = x * map.TileSize - regionX;
                double drawY = y * map.TileSize - regionY;

                dc.DrawImage(crop, new Rect(drawX, drawY, map.TileSize, map.TileSize));
            }

            if (map.Events is not null && map.Events.Count > 0)
            {
                var triggerFill = new SolidColorBrush(Color.FromArgb(220, 255, 215, 0)); triggerFill.Freeze();
                var npcFill = new SolidColorBrush(Color.FromArgb(220, 80, 170, 255)); npcFill.Freeze();
                var warpFill = new SolidColorBrush(Color.FromArgb(220, 190, 90, 255)); warpFill.Freeze();
                var outline = new Pen(Brushes.Black, 1); outline.Freeze();

                foreach (var ev in map.Events)
                {
                    if (ev.X < startTileX || ev.X >= endTileX || ev.Y < startTileY || ev.Y >= endTileY)
                        continue;

                    double left = ev.X * map.TileSize - regionX;
                    double top = ev.Y * map.TileSize - regionY;
                    var type = (ev.Type ?? "").Trim();

                    if (type.Equals("NPC", StringComparison.OrdinalIgnoreCase))
                    {
                        var c = new Point(left + map.TileSize / 2.0, top + map.TileSize / 2.0);
                        dc.DrawEllipse(npcFill, outline, c, map.TileSize * 0.22, map.TileSize * 0.22);
                    }
                    else if (type.Equals("Warp", StringComparison.OrdinalIgnoreCase))
                    {
                        var cx = left + map.TileSize / 2.0;
                        var cy = top + map.TileSize / 2.0;
                        double r = map.TileSize * 0.24;

                        var geo = new StreamGeometry();
                        using (var g = geo.Open())
                        {
                            g.BeginFigure(new Point(cx, cy - r), true, true);
                            g.LineTo(new Point(cx + r, cy), true, false);
                            g.LineTo(new Point(cx, cy + r), true, false);
                            g.LineTo(new Point(cx - r, cy), true, false);
                        }
                        geo.Freeze();
                        dc.DrawGeometry(warpFill, outline, geo);
                    }
                    else
                    {
                        dc.DrawRectangle(triggerFill, outline,
                            new Rect(left + map.TileSize * 0.28, top + map.TileSize * 0.28,
                                     map.TileSize * 0.44, map.TileSize * 0.44));
                    }
                }
            }

            if (_playerX >= startTileX && _playerX < endTileX && _playerY >= startTileY && _playerY < endTileY)
            {
                var fill = new SolidColorBrush(Color.FromArgb(230, 90, 180, 255)); fill.Freeze();
                var outline = new Pen(Brushes.Black, 1); outline.Freeze();

                double px = _playerX * map.TileSize - regionX;
                double py = _playerY * map.TileSize - regionY;

                dc.DrawEllipse(fill, outline,
                    new Point(px + map.TileSize / 2.0, py + map.TileSize / 2.0),
                    map.TileSize * 0.28,
                    map.TileSize * 0.28);
            }
        }

        var rtb = new RenderTargetBitmap(regionW, regionH, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();

        _viewportImage!.Source = rtb;
        _viewportImage.Width = regionW;
        _viewportImage.Height = regionH;

        Canvas.SetLeft(_viewportImage, regionX);
        Canvas.SetTop(_viewportImage, regionY);
    }

    private static int ClampToInt(double v)
    {
        if (v <= int.MinValue) return int.MinValue;
        if (v >= int.MaxValue) return int.MaxValue;
        return (int)Math.Floor(v);
    }

    private static BitmapSource LoadBitmap(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path, UriKind.Absolute);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    // ------------------------------------------------------------
    // Input + gameplay
    // ------------------------------------------------------------
    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (_map is null) return;

        if (e.Key == Key.Escape)
        {
            Close();
            return;
        }

        if (e.Key == Key.E || e.Key == Key.Enter || e.Key == Key.Space)
        {
            InteractAtPlayer();
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Up:
            case Key.W: TryMove(0, -1); e.Handled = true; return;
            case Key.Down:
            case Key.S: TryMove(0, 1); e.Handled = true; return;
            case Key.Left:
            case Key.A: TryMove(-1, 0); e.Handled = true; return;
            case Key.Right:
            case Key.D: TryMove(1, 0); e.Handled = true; return;
        }
    }

    private void TryMove(int dx, int dy)
    {
        var map = _map;
        if (map is null) return;

        int nx = _playerX + dx;
        int ny = _playerY + dy;

        if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height)
        {
            HudText.Text = "Blocked: edge of map.";
            return;
        }

        var col = GetCollisionLayer();
        if (col is not null && col.Tiles.Length == map.Width * map.Height)
        {
            int idx = ny * map.Width + nx;
            if (idx >= 0 && idx < col.Tiles.Length && col.Tiles[idx] != 0)
            {
                HudText.Text = "Blocked: collision tile.";
                return;
            }
        }

        _playerX = nx;
        _playerY = ny;

        var ev = GetEventAt(_playerX, _playerY);
        if (ev is not null)
        {
            if (string.Equals(ev.Type, "Warp", StringComparison.OrdinalIgnoreCase))
                DoWarp(ev);
            else if (string.Equals(ev.Type, "Trigger", StringComparison.OrdinalIgnoreCase))
                ExecuteEventScript(ev);
        }

        HudText.Text = $"Player({_playerX},{_playerY})";

        DeadzoneFollowPlayer();
        RequestRender();
    }

    private void InteractAtPlayer()
    {
        var ev = GetEventAt(_playerX, _playerY);
        if (ev is null)
        {
            HudText.Text = "Interact: no event here.";
            return;
        }

        if (string.Equals(ev.Type, "NPC", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ev.Type, "Trigger", StringComparison.OrdinalIgnoreCase))
        {
            ExecuteEventScript(ev);
            return;
        }

        HudText.Text = $"Interact: {ev.Name} ({ev.Type})";
    }

    private MapEventDto? GetEventAt(int x, int y)
    {
        var map = _map;
        if (map?.Events is null || map.Events.Count == 0) return null;
        return map.Events.FirstOrDefault(e => e.X == x && e.Y == y);
    }

    private void ExecuteEventScript(MapEventDto ev)
    {
        var cmds = ev.Commands ?? Array.Empty<EventCommandDto>();
        if (cmds.Count == 0)
        {
            HudText.Text = $"Event '{ev.Name}' has no commands.";
            return;
        }

        foreach (var cmd in cmds)
        {
            switch ((cmd.Command ?? "").Trim())
            {
                case "ShowText":
                {
                    cmd.Args.TryGetValue("text", out var text);
                    text ??= "";
                    if (text.Length == 0) text = "(empty text)";
                    MessageBox.Show(this, text, ev.Name);
                    break;
                }

                case "SetFlag":
                {
                    cmd.Args.TryGetValue("flag", out var flag);
                    cmd.Args.TryGetValue("value", out var value);
                    flag = (flag ?? "").Trim();
                    value ??= "";

                    if (flag.Length == 0)
                    {
                        HudText.Text = "SetFlag failed: flag is empty.";
                        break;
                    }

                    _flags[flag] = value;
                    HudText.Text = $"Flag set: {flag}='{value}'";
                    break;
                }

                default:
                    HudText.Text = $"Unknown command: {cmd.Command}";
                    break;
            }
        }
    }

    private void DoWarp(MapEventDto ev)
    {
        var map = _map;
        if (map is null) return;

        var p = ev.Parameters ?? new Dictionary<string, string>();

        p.TryGetValue("destMap", out var destMap);
        p.TryGetValue("destX", out var destXStr);
        p.TryGetValue("destY", out var destYStr);

        destMap = (destMap ?? "").Trim();
        if (string.IsNullOrWhiteSpace(destMap))
        {
            HudText.Text = "Warp failed: destMap missing.";
            return;
        }

        int.TryParse(destXStr, out var destX);
        int.TryParse(destYStr, out var destY);

        var destPath = IOPath.Combine(MapsFolder, destMap + MapFormat.DefaultExtension);

        if (!File.Exists(destPath))
        {
            HudText.Text = $"Warp failed: map not found '{destMap}'.";
            return;
        }

        LoadMap(destPath, $"Warp to {destMap}");

        if (_map is null) return;
        _playerX = Math.Clamp(destX, 0, _map.Width - 1);
        _playerY = Math.Clamp(destY, 0, _map.Height - 1);

        HudText.Text = $"Warped: {ev.Name} -> {destMap} ({_playerX},{_playerY})";

        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            CenterCameraOnPlayer();
            RequestRender();
            FocusGameSurface();
        }));
    }
}