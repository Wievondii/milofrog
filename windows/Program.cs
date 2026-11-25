using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--smoke-test")
        {
            var test = new Application();
            var pet = new PetWindow();
            pet.StartSmokeTest(args[1]);
            test.Run(pet);
            return;
        }

        bool created;
        using (var instance = new Mutex(true, "Local\\MilofrogDesktopPet", out created))
        {
            if (!created) return;
            new Application().Run(new PetWindow());
        }
    }
}

internal sealed class PetWindow : Window
{
    private enum Action { Idle, Right, Left, Wave, Laugh, Sad, Wait, Think, Review, Look, LookBack }

    private const int FramesPerSecond = 30;
    private const int AtlasColumns = 16;
    private static readonly int[] Counts = { 32, 23, 23, 19, 22, 35, 29, 28, 29, 8, 8 };
    private readonly BitmapSource[][] sprites = new BitmapSource[Counts.Length][];
    private readonly Image image = new Image { Stretch = Stretch.Uniform };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly DispatcherTimer clickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Forms.SystemInformation.DoubleClickTime) };
    private Action current;
    private long started;
    private long until;
    private long nextIdleGesture = 26000;
    private long lastDragMotion;
    private bool pressed;
    private bool dragged;
    private Point pointerDown;
    private Point lastPointer;
    private Point windowDown;
    private int clicks;

    internal PetWindow()
    {
        Title = "奶蛙桌宠";
        Width = 144;
        Height = 156;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Content = image;
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        LoadSprites();
        image.Source = sprites[0][0];
        image.ContextMenu = BuildMenu();
        MouseLeftButtonDown += OnDown;
        MouseLeftButtonUp += OnUp;
        MouseMove += OnMove;
        LostMouseCapture += delegate { pressed = false; };
        Loaded += delegate { ResetPosition(); CompositionTarget.Rendering += Render; };
        Closed += delegate { CompositionTarget.Rendering -= Render; clickTimer.Stop(); };
        clickTimer.Tick += delegate { ResolveClick(); };
    }

    private void LoadSprites()
    {
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("milofrog.png"))
        {
            var bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (bitmap.PixelWidth != AtlasColumns * 192 || bitmap.PixelHeight != 16 * 208)
                throw new InvalidDataException("奶蛙图集尺寸有误。");
            int index = 0;
            for (int row = 0; row < Counts.Length; row++)
            {
                sprites[row] = new BitmapSource[Counts[row]];
                for (int col = 0; col < Counts[row]; col++)
                {
                    var frame = new CroppedBitmap(bitmap, new Int32Rect(index % AtlasColumns * 192, index / AtlasColumns * 208, 192, 208));
                    frame.Freeze();
                    sprites[row][col] = frame;
                    index++;
                }
            }
        }
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        Add(menu, "挥手", () => Play(Action.Wave));
        Add(menu, "捧腹大笑", () => Play(Action.Laugh));
        Add(menu, "思考", () => Play(Action.Think));
        Add(menu, "观察", () => Play(Action.Review));
        Add(menu, "有点失落", () => Play(Action.Sad));
        menu.Items.Add(new Separator());
        var size = new MenuItem { Header = "大小" };
        Add(size, "小", () => ResizePet(108));
        Add(size, "中", () => ResizePet(144));
        Add(size, "大", () => ResizePet(192));
        menu.Items.Add(size);
        var pin = new MenuItem { Header = "始终置顶", IsCheckable = true, IsChecked = true };
        pin.Click += delegate { Topmost = pin.IsChecked; };
        menu.Items.Add(pin);
        Add(menu, "回到右下角", ResetPosition);
        menu.Items.Add(new Separator());
        Add(menu, "退出奶蛙", Close);
        return menu;
    }

    private static void Add(ItemsControl menu, string title, System.Action handler)
    {
        var item = new MenuItem { Header = title };
        item.Click += delegate { handler(); };
        menu.Items.Add(item);
    }

    private void ResizePet(double width)
    {
        var bottom = Top + Height;
        var right = Left + Width;
        Width = width;
        Height = width * 208 / 192;
        Left = right - Width;
        Top = bottom - Height;
    }

    private void ResetPosition()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 18;
        Top = area.Bottom - Height - 12;
    }

    private static Point CursorPixels()
    {
        var point = Forms.Cursor.Position;
        return new Point(point.X, point.Y);
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        pressed = true;
        dragged = false;
        clicks = e.ClickCount;
        clickTimer.Stop();
        pointerDown = lastPointer = CursorPixels();
        windowDown = new Point(Left, Top);
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!pressed || e.LeftButton != MouseButtonState.Pressed) return;
        var pointer = CursorPixels();
        var delta = pointer - pointerDown;
        if (!dragged && delta.Length < 5) return;
        dragged = true;
        var fromDevice = PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice;
        var offset = fromDevice.Transform(delta);
        Left = windowDown.X + offset.X;
        Top = windowDown.Y + offset.Y;
        var dx = pointer.X - lastPointer.X;
        if (Math.Abs(dx) >= 2) Play(dx > 0 ? Action.Right : Action.Left, true);
        lastPointer = pointer;
        lastDragMotion = clock.ElapsedMilliseconds;
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!pressed) return;
        pressed = false;
        ReleaseMouseCapture();
        nextIdleGesture = clock.ElapsedMilliseconds + 26000;
        if (dragged) Play(Action.Idle);
        else if (clicks >= 2) Play(Action.Laugh);
        else clickTimer.Start();
        e.Handled = true;
    }

    private void ResolveClick()
    {
        clickTimer.Stop();
        Play(Action.Wave);
    }

    private void Play(Action action, bool loop = false)
    {
        var now = clock.ElapsedMilliseconds;
        if (action != current || !loop) started = now;
        current = action;
        until = loop || action == Action.Idle ? long.MaxValue : now + (Counts[(int)action] * 1000 + FramesPerSecond - 1) / FramesPerSecond;
        nextIdleGesture = now + 26000;
    }

    private void Render(object sender, EventArgs e) { Draw(); }

    private void Draw()
    {
        var now = clock.ElapsedMilliseconds;
        if (now >= until || (dragged && pressed && now - lastDragMotion > 250 && (current == Action.Left || current == Action.Right)))
            Play(Action.Idle);
        if (current == Action.Idle && !pressed && now >= nextIdleGesture) Play(Action.Wait);

        var row = (int)current;
        var elapsed = Math.Max(0, now - started);
        var col = (int)(elapsed * FramesPerSecond / 1000 % Counts[row]);
        if (current == Action.Idle && !pressed)
        {
            var pointer = PointFromScreen(CursorPixels());
            var dx = pointer.X - ActualWidth / 2;
            var dy = pointer.Y - ActualHeight / 2;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance >= 60 && distance <= 300)
            {
                int direction = ((int)Math.Round(Math.Atan2(dx, -dy) * 8 / Math.PI) + 16) % 16;
                row = (int)Action.Look + direction / 8;
                col = direction % 8;
            }
        }
        var frame = sprites[row][col];
        if (image.Source != frame) image.Source = frame;
    }

    internal void StartSmokeTest(string report)
    {
        Loaded += delegate
        {
            try
            {
                if (!AllowsTransparency || !Topmost || !IsVisible || image.ContextMenu.Items.Count < 8)
                    throw new InvalidOperationException("窗口设置有误。");
                int total = 0;
                foreach (var row in sprites) total += row.Length;
                if (total != 256) throw new InvalidOperationException("帧数有误。");
                foreach (Action action in new[] { Action.Right, Action.Left, Action.Wave, Action.Laugh, Action.Sad, Action.Wait, Action.Think, Action.Review })
                {
                    Play(action);
                    Draw();
                    if (image.Source != sprites[(int)action][0]) throw new InvalidOperationException("动作映射有误。");
                }
                ResizePet(192);
                if (Width != 192 || Height != 208) throw new InvalidOperationException("缩放有误。");
                Play(Action.Right, true);
                int changes = 0;
                var measurement = Stopwatch.StartNew();
                var previous = image.Source;
                EventHandler measure = null;
                measure = delegate
                {
                    if (image.Source != previous) { changes++; previous = image.Source; }
                    if (measurement.ElapsedMilliseconds < 2000) return;
                    CompositionTarget.Rendering -= measure;
                    double fps = changes / measurement.Elapsed.TotalSeconds;
                    File.WriteAllText(report, String.Format("{0}：256 帧，跑步实测 {1:F1} FPS（{2} 次画面切换）。",
                        fps >= 28.5 && fps <= 31 ? "通过" : "失败", fps, changes));
                    Close();
                };
                CompositionTarget.Rendering += measure;
            }
            catch (Exception error)
            {
                File.WriteAllText(report, error.ToString());
                Close();
            }
        };
    }
}
