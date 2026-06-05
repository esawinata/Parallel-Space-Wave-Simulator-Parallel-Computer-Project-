using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ParallelSpaceWaveSimulator;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new GameForm());
    }
}

public sealed class GameForm : Form
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly Random _random = new(42);
    private readonly List<VisualEnemy> _visualEnemies = new();
    private readonly List<Projectile> _projectiles = new();
    private readonly List<Star> _stars = new();

    private Bitmap? _playerImage;
    private Bitmap? _enemyImage;
    private Bitmap? _projectileImage;

    private PointF _playerPosition;
    private bool _moveLeft;
    private bool _moveRight;
    private bool _moveUp;
    private bool _moveDown;
    private bool _shootHeld;
    private bool _isBenchmarkRunning;
    private bool _isGameOver;
    private int _shootCooldownFrames;

    private SimulationMode _runtimeMode = SimulationMode.Serial;
    private RuntimeEnemy[] _runtimeEnemies = Array.Empty<RuntimeEnemy>();
    private int _runtimeTurn;
    private double _lastFrameMs;
    private double _lastFps;
    private double _lastSimulationMs;
    private double _averageFps;
    private double _averageFrameMs;
    private double _averageSimulationMs;
    private double _fpsTotal;
    private double _frameMsTotal;
    private double _simulationMsTotal;
    private long _runtimeSampleCount;
    private int _lastRuntimeDefeated;
    private readonly Stopwatch _frameWatch = new();

    private int _score;
    private int _frame;
    private string _benchmarkText = "Tekan B untuk benchmark Serial vs Parallel";
    private BenchmarkResult? _lastSerial;
    private BenchmarkResult? _lastParallel;

    private const int PlayerDrawSize = 64;
    private const int EnemyDrawSize = 52;
    private const int ProjectileWidth = 6;
    private const int ProjectileHeight = 10;
    private const int ShootCooldownFrames = 4;
    private const int RuntimeEnemyCount = 80_000;

    public GameForm()
    {
        Text = "Parallel Space Wave Simulator - C# Komputasi Paralel";
        ClientSize = new Size(1000, 700);
        MinimumSize = new Size(900, 650);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        KeyPreview = true;
        BackColor = Color.Black;

        LoadAssets();
        ResetGameVisual();

        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        _timer.Tick += (_, _) => UpdateGame();
        _frameWatch.Start();
        _timer.Start();
    }

    private void LoadAssets()
    {
        // TrimTransparentMargins membuat sprite tidak mengecil karena kanvas PNG transparan terlalu besar.
        // Ini penting untuk Projectile.png: kanvasnya 100x100, tetapi gambar pelurunya kecil di tengah.
        _playerImage = AssetLoader.TrimTransparentMargins(AssetLoader.LoadBitmap("Player"));
        _enemyImage = AssetLoader.TrimTransparentMargins(AssetLoader.LoadBitmap("Enemy"));
        _projectileImage = AssetLoader.TrimTransparentMargins(AssetLoader.LoadBitmap("Projectile"));

        // Asset Enemy.png menghadap arah sebaliknya, jadi diputar 180 derajat
        // hanya di memory saat program berjalan. File asli tidak diubah.
        _enemyImage?.RotateFlip(RotateFlipType.Rotate180FlipNone);
    }

    private void ResetGameVisual()
    {
        _playerPosition = new PointF(ClientSize.Width / 2f - PlayerDrawSize / 2f, ClientSize.Height - 115);
        _visualEnemies.Clear();
        _projectiles.Clear();
        _score = 0;
        _isGameOver = false;
        _shootHeld = false;
        _shootCooldownFrames = 0;
        _benchmarkText = "Tekan B untuk benchmark Serial vs Parallel";
        _runtimeEnemies = RuntimeEnemySimulation.Generate(RuntimeEnemyCount);
        _runtimeTurn = 0;
        _lastSimulationMs = 0;
        _lastRuntimeDefeated = 0;
        ResetRuntimeAverages();

        for (int i = 0; i < 32; i++)
        {
            SpawnVisualEnemy(randomY: true);
        }

        _stars.Clear();
        for (int i = 0; i < 140; i++)
        {
            _stars.Add(new Star(
                X: _random.Next(ClientSize.Width),
                Y: _random.Next(ClientSize.Height),
                Speed: 0.4f + (float)_random.NextDouble() * 2.2f,
                Size: _random.Next(1, 3)
            ));
        }
    }

    private void SpawnVisualEnemy(bool randomY)
    {
        float x = _random.Next(30, Math.Max(31, ClientSize.Width - 80));
        float y = randomY ? _random.Next(-550, 80) : -70;
        float speed = 1.2f + (float)_random.NextDouble() * 2.3f;
        _visualEnemies.Add(new VisualEnemy(x, y, speed, Hp: 3));
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.A:
            case Keys.Left:
                _moveLeft = true;
                break;
            case Keys.D:
            case Keys.Right:
                _moveRight = true;
                break;
            case Keys.W:
            case Keys.Up:
                _moveUp = true;
                break;
            case Keys.S:
            case Keys.Down:
                // Jika tidak sedang benchmark, S tetap dipakai untuk gerak turun.
                _moveDown = true;
                break;
            case Keys.Space:
                _shootHeld = true;
                TryShoot();
                break;
            case Keys.R:
                ResetGameVisual();
                break;
            case Keys.Z:
                _runtimeMode = SimulationMode.Serial;
                ResetRuntimeAverages();
                _benchmarkText = "Runtime mode diganti ke SERIAL. Enemy update memakai for-loop biasa.";
                break;
            case Keys.X:
                _runtimeMode = SimulationMode.Parallel;
                ResetRuntimeAverages();
                _benchmarkText = "Runtime mode diganti ke PARALLEL. Enemy update memakai Parallel.For.";
                break;
            case Keys.B:
                await RunBenchmarkComparisonAsync();
                break;
            case Keys.D1:
                await RunSingleBenchmarkAsync(parallel: false);
                break;
            case Keys.D2:
                await RunSingleBenchmarkAsync(parallel: true);
                break;
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.A:
            case Keys.Left:
                _moveLeft = false;
                break;
            case Keys.D:
            case Keys.Right:
                _moveRight = false;
                break;
            case Keys.W:
            case Keys.Up:
                _moveUp = false;
                break;
            case Keys.S:
            case Keys.Down:
                _moveDown = false;
                break;
            case Keys.Space:
                _shootHeld = false;
                break;
        }
    }

    private void TryShoot()
    {
        if (_isGameOver) return;
        if (_shootCooldownFrames > 0) return;

        Shoot();
        _shootCooldownFrames = ShootCooldownFrames;
    }

    private void Shoot()
    {
        float bulletX = _playerPosition.X + PlayerDrawSize / 2f - ProjectileWidth / 2f;
        float bulletY = _playerPosition.Y + 8;
        _projectiles.Add(new Projectile(bulletX, bulletY, Speed: 11f));
    }

    private void UpdateGame()
    {
        _lastFrameMs = _frameWatch.Elapsed.TotalMilliseconds;
        _frameWatch.Restart();
        _lastFps = _lastFrameMs <= 0 ? 0 : 1000.0 / _lastFrameMs;

        _frame++;
        UpdateRuntimeSimulation();
        UpdateRuntimeAverages();
        UpdateStars();

        if (_shootCooldownFrames > 0)
        {
            _shootCooldownFrames--;
        }

        if (!_isGameOver)
        {
            UpdatePlayer();
            if (_shootHeld) TryShoot();
            UpdateProjectiles();
            UpdateVisualEnemies();
            CheckProjectileEnemyCollisions();
            CheckPlayerEnemyCollision();
        }

        Invalidate();
    }

    private void UpdateRuntimeSimulation()
    {
        if (_runtimeEnemies.Length == 0) return;

        Stopwatch stopwatch = Stopwatch.StartNew();
        _lastRuntimeDefeated = RuntimeEnemySimulation.Update(
            _runtimeEnemies,
            _runtimeMode == SimulationMode.Parallel,
            _runtimeTurn
        );
        stopwatch.Stop();

        _lastSimulationMs = stopwatch.Elapsed.TotalMilliseconds;
        _runtimeTurn++;
    }

    private void UpdateRuntimeAverages()
    {
        if (_lastFps <= 0 || _lastFrameMs <= 0) return;

        _runtimeSampleCount++;
        _fpsTotal += _lastFps;
        _frameMsTotal += _lastFrameMs;
        _simulationMsTotal += _lastSimulationMs;

        _averageFps = _fpsTotal / _runtimeSampleCount;
        _averageFrameMs = _frameMsTotal / _runtimeSampleCount;
        _averageSimulationMs = _simulationMsTotal / _runtimeSampleCount;
    }

    private void ResetRuntimeAverages()
    {
        _averageFps = 0;
        _averageFrameMs = 0;
        _averageSimulationMs = 0;
        _fpsTotal = 0;
        _frameMsTotal = 0;
        _simulationMsTotal = 0;
        _runtimeSampleCount = 0;
    }

    private void UpdatePlayer()
    {
        const float speed = 6.5f;
        if (_moveLeft) _playerPosition.X -= speed;
        if (_moveRight) _playerPosition.X += speed;
        if (_moveUp) _playerPosition.Y -= speed;
        if (_moveDown) _playerPosition.Y += speed;

        _playerPosition.X = Clamp(_playerPosition.X, 0, ClientSize.Width - PlayerDrawSize);
        _playerPosition.Y = Clamp(_playerPosition.Y, 130, ClientSize.Height - PlayerDrawSize - 10);
    }

    private void UpdateStars()
    {
        for (int i = 0; i < _stars.Count; i++)
        {
            Star star = _stars[i];
            star.Y += star.Speed;
            if (star.Y > ClientSize.Height)
            {
                star.Y = 0;
                star.X = _random.Next(ClientSize.Width);
            }
            _stars[i] = star;
        }
    }

    private void UpdateProjectiles()
    {
        for (int i = _projectiles.Count - 1; i >= 0; i--)
        {
            Projectile projectile = _projectiles[i];
            projectile.Y -= projectile.Speed;
            if (projectile.Y < -ProjectileHeight)
            {
                _projectiles.RemoveAt(i);
            }
            else
            {
                _projectiles[i] = projectile;
            }
        }
    }

    private void UpdateVisualEnemies()
    {
        for (int i = _visualEnemies.Count - 1; i >= 0; i--)
        {
            VisualEnemy enemy = _visualEnemies[i];
            enemy.Y += enemy.Speed;
            enemy.X += MathF.Sin((_frame + i * 13) * 0.035f) * 0.8f;

            if (enemy.Y > ClientSize.Height + EnemyDrawSize)
            {
                _visualEnemies.RemoveAt(i);
                SpawnVisualEnemy(randomY: false);
            }
            else
            {
                _visualEnemies[i] = enemy;
            }
        }
    }

    private void CheckProjectileEnemyCollisions()
    {
        for (int p = _projectiles.Count - 1; p >= 0; p--)
        {
            RectangleF projectileHitbox = new(
                _projectiles[p].X,
                _projectiles[p].Y,
                ProjectileWidth,
                ProjectileHeight
            );

            bool projectileRemoved = false;
            for (int e = _visualEnemies.Count - 1; e >= 0; e--)
            {
                RectangleF enemyHitbox = new(
                    _visualEnemies[e].X + 8,
                    _visualEnemies[e].Y + 8,
                    EnemyDrawSize - 16,
                    EnemyDrawSize - 16
                );

                if (!projectileHitbox.IntersectsWith(enemyHitbox)) continue;

                VisualEnemy enemy = _visualEnemies[e];
                enemy.Hp--;

                if (enemy.Hp <= 0)
                {
                    _visualEnemies.RemoveAt(e);
                    SpawnVisualEnemy(randomY: false);
                    _score++;
                }
                else
                {
                    _visualEnemies[e] = enemy;
                }

                _projectiles.RemoveAt(p);
                projectileRemoved = true;
                break;
            }

            if (projectileRemoved)
            {
                continue;
            }
        }
    }

    private void CheckPlayerEnemyCollision()
    {
        RectangleF playerHitbox = new(
            _playerPosition.X + 12,
            _playerPosition.Y + 12,
            PlayerDrawSize - 24,
            PlayerDrawSize - 24
        );

        foreach (VisualEnemy enemy in _visualEnemies)
        {
            RectangleF enemyHitbox = new(
                enemy.X + 8,
                enemy.Y + 8,
                EnemyDrawSize - 16,
                EnemyDrawSize - 16
            );

            if (!playerHitbox.IntersectsWith(enemyHitbox)) continue;

            _isGameOver = true;
            _shootHeld = false;
            _projectiles.Clear();
            _benchmarkText = "GAME OVER: player collide/overlap dengan enemy. Tekan R untuk restart.";
            return;
        }
    }

    private async Task RunBenchmarkComparisonAsync()
    {
        if (_isBenchmarkRunning) return;
        _isBenchmarkRunning = true;
        _benchmarkText = "Benchmark berjalan: mode serial lalu mode paralel...";
        Invalidate();

        const int enemyCount = 1_000_000;
        const int turns = 20;

        _lastSerial = await Task.Run(() => EnemyBenchmark.Run(parallel: false, enemyCount, turns));
        _benchmarkText = "Serial selesai. Melanjutkan benchmark paralel...";
        Invalidate();

        _lastParallel = await Task.Run(() => EnemyBenchmark.Run(parallel: true, enemyCount, turns));
        double speedup = _lastParallel.ElapsedMilliseconds <= 0
            ? 0
            : (double)_lastSerial.ElapsedMilliseconds / _lastParallel.ElapsedMilliseconds;

        _benchmarkText =
            $"Benchmark selesai | Serial: {_lastSerial.ElapsedMilliseconds} ms | " +
            $"Parallel: {_lastParallel.ElapsedMilliseconds} ms | Speedup: {speedup:F2}x | " +
            $"Defeated: {_lastParallel.DefeatedEnemies:N0}/{enemyCount:N0}";

        _isBenchmarkRunning = false;
        Invalidate();
    }

    private async Task RunSingleBenchmarkAsync(bool parallel)
    {
        if (_isBenchmarkRunning) return;
        _isBenchmarkRunning = true;
        string mode = parallel ? "Parallel" : "Serial";
        _benchmarkText = $"Benchmark {mode} berjalan...";
        Invalidate();

        BenchmarkResult result = await Task.Run(() => EnemyBenchmark.Run(parallel, enemyCount: 1_000_000, turns: 20));
        if (parallel) _lastParallel = result;
        else _lastSerial = result;

        _benchmarkText =
            $"{mode} selesai | Time: {result.ElapsedMilliseconds} ms | " +
            $"Defeated: {result.DefeatedEnemies:N0} | Alive: {result.AliveEnemies:N0} | " +
            $"CPU Threads: {Environment.ProcessorCount}";

        _isBenchmarkRunning = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        DrawBackground(g);
        DrawSprites(g);
        DrawOverlay(g);
    }

    private void DrawBackground(Graphics g)
    {
        using LinearGradientBrush brush = new(
            ClientRectangle,
            Color.FromArgb(8, 10, 28),
            Color.FromArgb(0, 0, 0),
            LinearGradientMode.Vertical
        );
        g.FillRectangle(brush, ClientRectangle);

        using SolidBrush starBrush = new(Color.White);
        foreach (Star star in _stars)
        {
            int alpha = star.Size == 1 ? 130 : 210;
            using SolidBrush b = new(Color.FromArgb(alpha, Color.White));
            g.FillEllipse(b, star.X, star.Y, star.Size, star.Size);
        }
    }

    private void DrawSprites(Graphics g)
    {
        foreach (VisualEnemy enemy in _visualEnemies)
        {
            RectangleF dest = new(enemy.X, enemy.Y, EnemyDrawSize, EnemyDrawSize);
            DrawImageOrFallback(g, _enemyImage, dest, Color.DeepSkyBlue, "E");
        }

        foreach (Projectile projectile in _projectiles)
        {
            RectangleF dest = new(projectile.X, projectile.Y, ProjectileWidth, ProjectileHeight);
            DrawImageOrFallback(g, _projectileImage, dest, Color.Cyan, "|");
        }

        RectangleF playerDest = new(_playerPosition.X, _playerPosition.Y, PlayerDrawSize, PlayerDrawSize);
        DrawImageOrFallback(g, _playerImage, playerDest, Color.IndianRed, "P");
    }

    private static void DrawImageOrFallback(Graphics g, Bitmap? image, RectangleF dest, Color fallbackColor, string label)
    {
        if (image is not null)
        {
            g.DrawImage(image, dest);
            return;
        }

        using SolidBrush brush = new(fallbackColor);
        using Pen pen = new(Color.White, 2);
        g.FillEllipse(brush, dest);
        g.DrawEllipse(pen, dest.X, dest.Y, dest.Width, dest.Height);
        using Font font = new("Consolas", 12, FontStyle.Bold);
        using SolidBrush textBrush = new(Color.White);
        SizeF size = g.MeasureString(label, font);
        g.DrawString(label, font, textBrush, dest.X + dest.Width / 2 - size.Width / 2, dest.Y + dest.Height / 2 - size.Height / 2);
    }

    private void DrawOverlay(Graphics g)
    {
        Rectangle panel = new(15, 15, ClientSize.Width - 30, 130);
        using SolidBrush panelBrush = new(Color.FromArgb(190, 5, 8, 20));
        using Pen panelPen = new(Color.FromArgb(130, 70, 220, 255));
        g.FillRectangle(panelBrush, panel);
        g.DrawRectangle(panelPen, panel);

        using Font titleFont = new("Consolas", 15, FontStyle.Bold);
        using Font font = new("Consolas", 10, FontStyle.Regular);
        using SolidBrush white = new(Color.White);
        using SolidBrush cyan = new(Color.Cyan);
        using SolidBrush yellow = new(Color.Khaki);

        g.DrawString("Parallel Space Wave Simulator", titleFont, cyan, 30, 25);
        g.DrawString("WASD/Arrow: move | Hold Space: auto shoot | Z: Serial runtime | X: Parallel runtime | B: benchmark | R: reset", font, white, 32, 55);
        g.DrawString($"Mode: {_runtimeMode} | FPS now: {_lastFps:F1} | Avg FPS: {_averageFps:F1} | Frame: {_lastFrameMs:F2} ms | Avg frame: {_averageFrameMs:F2} ms", font, white, 32, 78);
        g.DrawString($"Runtime simulation: {_lastSimulationMs:F2} ms | Avg simulation: {_averageSimulationMs:F2} ms | Runtime enemies: {_runtimeEnemies.Length:N0} | Runtime defeated: {_lastRuntimeDefeated:N0}", font, white, 32, 99);
        g.DrawString($"Visual enemies: {_visualEnemies.Count} | Projectiles: {_projectiles.Count} | Score: {_score} | Samples: {_runtimeSampleCount:N0} | Status: {(_isGameOver ? "GAME OVER" : "PLAYING")}", font, white, 32, 120);
        g.DrawString(_benchmarkText, font, yellow, 32, 141);

        if (_isGameOver)
        {
            using Font gameOverFont = new("Consolas", 42, FontStyle.Bold);
            using SolidBrush red = new(Color.FromArgb(230, Color.OrangeRed));
            using SolidBrush shadow = new(Color.FromArgb(160, Color.Black));
            string text = "GAME OVER";
            SizeF size = g.MeasureString(text, gameOverFont);
            float x = ClientSize.Width / 2f - size.Width / 2f;
            float y = ClientSize.Height / 2f - size.Height / 2f;
            g.DrawString(text, gameOverFont, shadow, x + 4, y + 4);
            g.DrawString(text, gameOverFont, red, x, y);
            g.DrawString("Player overlap dengan enemy. Tekan R untuk restart.", font, white, x + 8, y + 70);
        }

        if (_lastSerial is not null || _lastParallel is not null)
        {
            string serial = _lastSerial is null ? "-" : $"{_lastSerial.ElapsedMilliseconds} ms";
            string parallel = _lastParallel is null ? "-" : $"{_lastParallel.ElapsedMilliseconds} ms";
            string speedup = "-";
            if (_lastSerial is not null && _lastParallel is not null && _lastParallel.ElapsedMilliseconds > 0)
            {
                speedup = $"{(double)_lastSerial.ElapsedMilliseconds / _lastParallel.ElapsedMilliseconds:F2}x";
            }

            g.DrawString($"Last result => Serial: {serial} | Parallel: {parallel} | Speedup: {speedup}", font, yellow, 32, ClientSize.Height - 35);
        }
    }

    private static float Clamp(float value, float min, float max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}

public static class AssetLoader
{
    public static Bitmap? TrimTransparentMargins(Bitmap? source)
    {
        if (source is null) return null;

        int left = source.Width;
        int top = source.Height;
        int right = -1;
        int bottom = -1;

        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                Color pixel = source.GetPixel(x, y);
                if (pixel.A <= 10) continue;

                if (x < left) left = x;
                if (y < top) top = y;
                if (x > right) right = x;
                if (y > bottom) bottom = y;
            }
        }

        if (right < left || bottom < top)
        {
            return source;
        }

        Rectangle crop = Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        if (crop.Width == source.Width && crop.Height == source.Height)
        {
            return source;
        }

        Bitmap trimmed = new(crop.Width, crop.Height);
        using Graphics g = Graphics.FromImage(trimmed);
        g.Clear(Color.Transparent);
        g.DrawImage(
            source,
            new Rectangle(0, 0, crop.Width, crop.Height),
            crop,
            GraphicsUnit.Pixel
        );

        source.Dispose();
        return trimmed;
    }

    public static Bitmap? LoadBitmap(string assetName)
    {
        string baseDir = AppContext.BaseDirectory;
        string currentDir = Environment.CurrentDirectory;

        string[] candidates =
        [
            Path.Combine(baseDir, "Assets", assetName + ".png"),
            Path.Combine(baseDir, "Assets", assetName),
            Path.Combine(baseDir, "Assets", assetName, assetName + ".png"),
            Path.Combine(currentDir, "Assets", assetName + ".png"),
            Path.Combine(currentDir, "Assets", assetName),
            Path.Combine(currentDir, "Assets", assetName, assetName + ".png")
        ];

        foreach (string path in candidates)
        {
            if (!File.Exists(path)) continue;
            return new Bitmap(path);
        }

        // Fallback tambahan: cari file yang namanya Player/Enemy/Projectile dengan ekstensi apa pun.
        foreach (string root in new[] { Path.Combine(baseDir, "Assets"), Path.Combine(currentDir, "Assets") })
        {
            if (!Directory.Exists(root)) continue;

            string? found = Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .FirstOrDefault(file =>
                    Path.GetFileNameWithoutExtension(file).Equals(assetName, StringComparison.OrdinalIgnoreCase));

            if (found is not null)
            {
                return new Bitmap(found);
            }
        }

        MessageBox.Show(
            $"Asset '{assetName}' tidak ditemukan. Pastikan path seperti ini: Assets/{assetName}.png",
            "Asset Missing",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning
        );
        return null;
    }
}

public enum SimulationMode
{
    Serial,
    Parallel
}

public static class RuntimeEnemySimulation
{
    public static RuntimeEnemy[] Generate(int count)
    {
        RuntimeEnemy[] enemies = new RuntimeEnemy[count];
        for (int i = 0; i < count; i++)
        {
            enemies[i] = new RuntimeEnemy
            {
                X = (i * 37) % 1000,
                Y = (i * 53) % 700,
                Vx = (((i * 17) % 9) - 4) * 0.10f,
                Vy = 0.25f + ((i * 29) % 100) / 300f,
                Hp = 80 + (i * 11) % 140,
                Defense = 3 + (i * 7) % 25,
                Alive = true
            };
        }
        return enemies;
    }

    public static int Update(RuntimeEnemy[] enemies, bool parallel, int turn)
    {
        if (parallel)
        {
            Parallel.For(0, enemies.Length, i =>
            {
                RuntimeEnemy enemy = enemies[i];
                UpdateOne(ref enemy, i, turn);
                enemies[i] = enemy;
            });
        }
        else
        {
            for (int i = 0; i < enemies.Length; i++)
            {
                UpdateOne(ref enemies[i], i, turn);
            }
        }

        int defeated = 0;
        for (int i = 0; i < enemies.Length; i++)
        {
            if (!enemies[i].Alive) defeated++;
        }
        return defeated;
    }

    private static void UpdateOne(ref RuntimeEnemy enemy, int index, int turn)
    {
        if (!enemy.Alive) return;

        enemy.X += enemy.Vx + MathF.Sin((index + turn) * 0.016f) * 0.030f;
        enemy.Y += enemy.Vy + MathF.Cos((index - turn) * 0.012f) * 0.030f;

        if (enemy.X < 0) enemy.X += 1000;
        if (enemy.X > 1000) enemy.X -= 1000;
        if (enemy.Y > 700) enemy.Y -= 700;

        float dx = enemy.X - 500f;
        float dy = enemy.Y - 350f;
        float distanceSquared = dx * dx + dy * dy;
        float radius = 420f + (turn % 5) * 18f;
        if (distanceSquared > radius * radius) return;

        int damage = 5 + ((index * 31 + turn * 17) % 25);
        int finalDamage = Math.Max(1, damage - enemy.Defense / 3);
        enemy.Hp -= finalDamage;

        if (enemy.Hp <= 0)
        {
            enemy.Hp = 0;
            enemy.Alive = false;
        }
    }
}

public static class EnemyBenchmark
{
    public static BenchmarkResult Run(bool parallel, int enemyCount, int turns)
    {
        EnemyData[] enemies = GenerateEnemies(enemyCount);
        Stopwatch stopwatch = Stopwatch.StartNew();

        if (parallel)
        {
            for (int turn = 0; turn < turns; turn++)
            {
                int currentTurn = turn;
                Parallel.For(0, enemies.Length, i =>
                {
                    EnemyData enemy = enemies[i];
                    UpdateEnemy(ref enemy, i, currentTurn);
                    enemies[i] = enemy;
                });
            }
        }
        else
        {
            for (int turn = 0; turn < turns; turn++)
            {
                for (int i = 0; i < enemies.Length; i++)
                {
                    UpdateEnemy(ref enemies[i], i, turn);
                }
            }
        }

        stopwatch.Stop();

        int defeated = 0;
        long remainingHp = 0;
        for (int i = 0; i < enemies.Length; i++)
        {
            if (!enemies[i].Alive) defeated++;
            if (enemies[i].Hp > 0) remainingHp += enemies[i].Hp;
        }

        return new BenchmarkResult(
            Mode: parallel ? "Parallel.For" : "Serial for-loop",
            EnemyCount: enemyCount,
            Turns: turns,
            ElapsedMilliseconds: stopwatch.ElapsedMilliseconds,
            DefeatedEnemies: defeated,
            AliveEnemies: enemyCount - defeated,
            RemainingHpChecksum: remainingHp,
            ProcessorCount: Environment.ProcessorCount
        );
    }

    private static EnemyData[] GenerateEnemies(int count)
    {
        EnemyData[] enemies = new EnemyData[count];
        for (int i = 0; i < count; i++)
        {
            enemies[i] = new EnemyData
            {
                X = (i * 37) % 1000,
                Y = (i * 53) % 700,
                Vx = (((i * 17) % 9) - 4) * 0.11f,
                Vy = 0.35f + ((i * 29) % 100) / 250f,
                Hp = 120 + (i * 11) % 180,
                Defense = 4 + (i * 7) % 35,
                Alive = true
            };
        }
        return enemies;
    }

    private static void UpdateEnemy(ref EnemyData enemy, int index, int turn)
    {
        if (!enemy.Alive) return;

        // Simulasi update posisi enemy dalam game.
        enemy.X += enemy.Vx + MathF.Sin((index + turn) * 0.013f) * 0.025f;
        enemy.Y += enemy.Vy + MathF.Cos((index - turn) * 0.011f) * 0.025f;

        // Simulasi serangan area dari player.
        float dx = enemy.X - 500f;
        float dy = enemy.Y - 350f;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        bool insideAttackArea = distance < 520f + (turn % 4) * 35f;

        if (!insideAttackArea) return;

        int baseDamage = 18 + ((index * 31 + turn * 17) % 70);
        bool criticalHit = ((index + turn * 19) % 23) == 0;
        if (criticalHit) baseDamage *= 2;

        int finalDamage = Math.Max(1, baseDamage - enemy.Defense / 2);
        enemy.Hp -= finalDamage;

        if (enemy.Hp <= 0)
        {
            enemy.Hp = 0;
            enemy.Alive = false;
        }
    }
}

public struct RuntimeEnemy
{
    public float X;
    public float Y;
    public float Vx;
    public float Vy;
    public int Hp;
    public int Defense;
    public bool Alive;
}

public struct EnemyData
{
    public float X;
    public float Y;
    public float Vx;
    public float Vy;
    public int Hp;
    public int Defense;
    public bool Alive;
}

public sealed record BenchmarkResult(
    string Mode,
    int EnemyCount,
    int Turns,
    long ElapsedMilliseconds,
    int DefeatedEnemies,
    int AliveEnemies,
    long RemainingHpChecksum,
    int ProcessorCount
);

public record struct VisualEnemy(float X, float Y, float Speed, int Hp);
public record struct Projectile(float X, float Y, float Speed);
public record struct Star(float X, float Y, float Speed, int Size);
