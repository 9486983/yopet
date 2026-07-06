using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using yopet.Core.Models;
using yopet.ViewModels;
using SkiaSharp;
using System.Diagnostics;

namespace yopet.Views;

public partial class PetdexDialog : Window
{
    private PetViewModel? _vm;
    private static readonly string ThumbDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".petdex", "thumbs");

    /// <summary>获取当前主题色（带 fallback）</summary>
    private static SolidColorBrush ThemeBrush(string resourceKey, uint fallbackHex = 0xFFFFFFFF)
    {
        if (Application.Current?.TryFindResource(resourceKey, out var value) == true && value is Color c)
            return new SolidColorBrush(c);
        return new SolidColorBrush(Color.Parse($"#{fallbackHex:X8}"));
    }

    public PetdexDialog()
    {
        InitializeComponent();
        CloseBtn.Click += (_, _) => Close();

        // 安装按钮
        InstallBtn.Click += (_, _) => StartInstall();
        InstallInput.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) StartInstall();
        };

        // 刷新
        ReloadBtn.Click += (_, _) => ReloadPets();
    }

    public void LoadPets(PetViewModel vm)
    {
        _vm = vm;
        ReloadPets();
    }

    private void ReloadPets()
    {
        _vm?.ReloadPetdexPets();
        PetGrid.Children.Clear();
        var pets = _vm?.PetdexPets ?? [];

        if (pets.Count > 0)
        {
            PetdexHint.IsVisible = false;
            // 先确保所有缩略图已生成，再创建卡片
            foreach (var pet in pets)
                GenerateThumbnail(pet);
            foreach (var pet in pets)
                PetGrid.Children.Add(CreatePetCard(pet));
        }
        else
        {
            PetdexHint.IsVisible = true;
        }
    }

    // ── 安装 ──

    private async void StartInstall()
    {
        var raw = InstallInput.Text?.Trim();
        if (string.IsNullOrEmpty(raw)) return;

        // 只取最后一个参数作为宠物名
        var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var petName = parts[^1];

        InstallBtn.IsEnabled = false;
        InstallInput.IsEnabled = false;
        InstallStatus.Text = $"⏳ 正在安装 {petName} ...";
        InstallStatus.IsVisible = true;
        InstallStatus.Foreground = new SolidColorBrush(Color.Parse("#88FFFFFF"));

        try
        {
            InstallStatus.Text = $"⏳ 正在安装 {petName} （首次会下载 npx 包，请稍候）...";

            // 读取输出，避免缓冲区满导致进程阻塞
            var output = new System.Text.StringBuilder();

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c npx --yes petdex install {petName}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var proc = new System.Diagnostics.Process { StartInfo = psi };
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // 带超时，防止 npx 卡死
            var exitTask = proc.WaitForExitAsync();
            var completed = await Task.WhenAny(exitTask, Task.Delay(120_000));
            if (completed != exitTask)
            {
                proc.Kill();
                InstallStatus.Text = $"⏱️ 安装超时（120秒），请检查网络后重试";
                InstallStatus.Foreground = new SolidColorBrush(Color.Parse("#FF8888"));
                return;
            }

            if (proc.ExitCode == 0)
            {
                InstallStatus.Text = $"✅ {petName} 安装成功！";
                InstallStatus.Foreground = new SolidColorBrush(Color.Parse("#88FF88"));
                InstallInput.Text = "";
            }
            else
            {
                var err = output.ToString().Trim();
                if (err.Length > 100) err = err[..100] + "...";
                InstallStatus.Text = $"❌ 安装失败: {err}";
                InstallStatus.Foreground = new SolidColorBrush(Color.Parse("#FF8888"));
            }
        }
        catch (Exception ex)
        {
            InstallStatus.Text = $"❌ 错误: {ex.Message}";
            InstallStatus.Foreground = new SolidColorBrush(Color.Parse("#FF8888"));
        }
        finally
        {
            InstallBtn.IsEnabled = true;
            InstallInput.IsEnabled = true;
            ReloadPets(); // 安装后自动刷新
        }
    }

    // ── 卡片 ──

    private Border CreatePetCard(PetDefinition pet)
    {
        var bgCard = ThemeBrush("BgCard", 0xFF252540);
        var bgHover = ThemeBrush("BgHover", 0xFF3D3D5C);
        var textPrimary = ThemeBrush("TextPrimary", 0xFFFFFFFF);

        var border = new Border
        {
            Width = 100,
            Height = 110,
            CornerRadius = new CornerRadius(12),
            Background = bgCard,
            Margin = new Thickness(4),
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        var stack = new StackPanel
        {
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };

        // 加载缓存的缩略图
        var thumbPath = GetThumbPath(pet.Id);
        if (File.Exists(thumbPath))
        {
            try
            {
                var bmp = new Bitmap(thumbPath);
                var img = new Image
                {
                    Source = bmp,
                    Width = 80,
                    Height = 86,
                    Stretch = Avalonia.Media.Stretch.Uniform,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                };
                stack.Children.Add(img);
            }
            catch
            {
                stack.Children.Add(new TextBlock { Text = "🎮", FontSize = 36, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center });
            }
        }
        else
        {
            stack.Children.Add(new TextBlock { Text = "🎮", FontSize = 36, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center });
        }

        var name = new TextBlock
        {
            Text = pet.Name,
            FontSize = 12,
            Foreground = textPrimary,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };
        stack.Children.Add(name);
        border.Child = stack;

        border.PointerEntered += (_, _) =>
            border.Background = bgHover;
        border.PointerExited += (_, _) =>
            border.Background = bgCard;
        border.PointerPressed += (_, _) =>
        {
            _vm?.SelectPetCommand.Execute(pet);
            Close();
        };

        return border;
    }

    // ── 缩略图缓存 ──

    private static string GetThumbPath(string petId)
    {
        var slug = petId.Replace("petdex:", "");
        return Path.Combine(ThumbDir, $"{slug}.png");
    }

    /// <summary>提取 spritesheet 第一帧，缩小后缓存为 PNG</summary>
    private static void GenerateThumbnail(PetDefinition pet)
    {
        var slug = pet.Id.Replace("petdex:", "");
        var thumbPath = GetThumbPath(pet.Id);

        if (File.Exists(thumbPath)) return;

        if (!Directory.Exists(ThumbDir))
            Directory.CreateDirectory(ThumbDir);

        try
        {
            using var src = SKBitmap.Decode(pet.SpritesheetPath);
            if (src == null) return;

            // 提取第一帧（行0,列0）
            using var frame = new SKBitmap(pet.FrameWidth, pet.FrameHeight);
            src.ExtractSubset(frame, new SKRectI(0, 0, pet.FrameWidth, pet.FrameHeight));

            // 缩放到缩略图尺寸（保留宽高比）
            var thumbW = 80;
            var thumbH = 86;
            using var resized = frame.Resize(new SKImageInfo(thumbW, thumbH), new SKSamplingOptions(SKFilterMode.Linear));
            if (resized == null) return;

            using var image = SKImage.FromBitmap(resized);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            using var fs = File.OpenWrite(thumbPath);
            data.SaveTo(fs);
        }
        catch { }
    }
}
