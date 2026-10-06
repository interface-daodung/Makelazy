using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Makelazy.App.Models;
using Makelazy.App.Services;

namespace Makelazy.App;

/// <summary>Nối string[] bằng separator (mặc định space).</summary>
public sealed class StringJoinConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is System.Collections.IEnumerable e && value is not string)
        {
            var sep = parameter as string ?? " ";
            return string.Join(sep, e.Cast<object>());
        }
        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>string rỗng / mảng rỗng -> Collapsed.</summary>
public sealed class NonEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool any = value switch
        {
            string s => !string.IsNullOrWhiteSpace(s),
            System.Collections.ICollection c => c.Count > 0,
            System.Collections.IEnumerable e => e.Cast<object>().Any(),
            _ => value is not null,
        };
        return any ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public partial class MainWindow : Window
{
    private sealed class NativeSession
    {
        public required string Id { get; init; }
        public required MakeSession Proc { get; init; }
        public required TabItem Tab { get; init; }
        public required RichTextBox Output { get; init; }
        public required AnsiWriter Writer { get; init; }
        public required System.Windows.Shapes.Ellipse Dot { get; init; }
        public required TextBlock ExitText { get; init; }
        public required Button StopButton { get; init; }
    }

    private static readonly Brush RunningBrush = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
    private static readonly Brush OkBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));
    private static readonly Brush FailBrush = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
    private static readonly Brush KilledBrush = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));

    private readonly Dictionary<string, NativeSession> _sessions = new();
    private readonly List<MakefileTarget> _allTargets = new();
    private string? _currentMakefile;
    private readonly DispatcherTimer _statusTimer;

    /// <summary>File Makefile truyền qua CLI / Open-With / single-instance. App.xaml gán trước khi Show().</summary>
    public string? PendingMakefile { get; set; }
    /// <summary>Target tự chạy sau khi mở file (CLI --target).</summary>
    public string? PendingTarget { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        Drop += OnDrop;
        Loaded += (_, _) =>
        {
            RefreshPinButton();
            AutoLoadFromArgs();
        };
        Closed += (_, _) =>
        {
            foreach (var s in _sessions.Values) { try { s.Proc.Dispose(); } catch { } }
        };
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            StatusText.Text = "Terminal ẩn (CreateNoWindow) · stream trực tiếp · gõ stdin ở ô dưới mỗi tab";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
        };
    }

    // ---------- Mở file ----------
    private void AutoLoadFromArgs()
    {
        if (!string.IsNullOrEmpty(PendingMakefile) && File.Exists(PendingMakefile))
        {
            LoadMakefile(PendingMakefile);
            return;
        }
        var args = Environment.GetCommandLineArgs();
        foreach (var a in args.Skip(1))
        {
            var p = CliOptions.ResolveMakefilePath(a);
            if (p is not null) { LoadMakefile(p); return; }
        }
        var local = Path.Combine(Environment.CurrentDirectory, "Makefile");
        if (File.Exists(local)) LoadMakefile(local);
    }

    /// <summary>Instance 2 (double-click file khi app đang mở) chuyển file sang qua named pipe.</summary>
    public void OpenExternalFile(string? path, string? target)
    {
        var resolved = CliOptions.ResolveMakefilePath(path);
        if (resolved is null)
        {
            ShowStatus("Không mở được file (không phải Makefile hợp lệ).", isError: true);
            Activate();
            return;
        }
        PendingTarget = string.IsNullOrWhiteSpace(target) ? null : target;
        LoadMakefile(resolved);
        Activate();
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files.Length > 0)
        {
            var p = CliOptions.ResolveMakefilePath(files[0]);
            if (p is not null) LoadMakefile(p);
        }
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e) => OpenMakefileDialog();

    private void OpenMakefileDialog()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Mở Makefile (file tên Makefile, không đuôi)",
            Filter = "Makefile|Makefile*;Makefile|All files|*.*",
            FileName = "Makefile",
        };
        if (dlg.ShowDialog(this) == true)
            LoadMakefile(dlg.FileName);
    }

    public void LoadMakefile(string path)
    {
        try
        {
            _currentMakefile = Path.GetFullPath(path);
            var targets = MakefileParser.Parse(_currentMakefile);
            _allTargets.Clear();
            _allTargets.AddRange(targets);
            FilePathText.Text = _currentMakefile;
            FilePathText.ToolTip = _currentMakefile;
            TargetCountText.Text = targets.Count.ToString();
            ApplyFilter();
            if (targets.Count == 0)
                ShowStatus("Không tìm thấy target nào. Kiểm tra cú pháp 'ten_target: deps' + recipe thụt tab.", isError: true);
            else
                ShowStatus($"Đã mở {targets.Count} targets từ {Path.GetFileName(_currentMakefile)}.", isError: false);

            if (!string.IsNullOrWhiteSpace(PendingTarget))
            {
                var auto = PendingTarget;
                PendingTarget = null;
                RunTarget(auto);
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"Không đọc được Makefile: {ex.Message}", isError: true);
        }
    }

    // ---------- Danh sách target ----------
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var q = (SearchBox.Text ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(q))
        {
            TargetList.ItemsSource = _allTargets.ToList();
            return;
        }
        TargetList.ItemsSource = _allTargets.Where(t =>
            t.Name.ToLowerInvariant().Contains(q)
            || t.Description.ToLowerInvariant().Contains(q)
            || t.Commands.Any(c => c.ToLowerInvariant().Contains(q))).ToList();
    }

    private void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string name && !string.IsNullOrWhiteSpace(name))
            RunTarget(name);
    }

    // ---------- Chạy process ----------
    private void RunTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(_currentMakefile))
        {
            ShowStatus("Chưa mở Makefile nào.", isError: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(target)) return;

        var recipe = _allTargets.FirstOrDefault(t => t.Name == target)?.Commands ?? Array.Empty<string>();
        MakeSession proc;
        try
        {
            proc = new MakeSession(_currentMakefile, target, recipe);
        }
        catch (Exception ex)
        {
            ShowStatus($"Không chạy được: {ex.Message}", isError: true);
            return;
        }

        var tab = BuildSessionTab(target, proc.SessionId, out var output, out var writer,
            out var dot, out var exitText, out var stopButton, out var input);
        var ns = new NativeSession
        {
            Id = proc.SessionId, Proc = proc, Tab = tab, Output = output,
            Writer = writer, Dot = dot, ExitText = exitText, StopButton = stopButton,
        };
        _sessions[proc.SessionId] = ns;
        SessionTabs.Items.Add(tab);
        SessionTabs.SelectedItem = tab;
        EmptyHint.Visibility = Visibility.Collapsed;

        proc.OnOutput += (_, data) => Dispatcher.InvokeAsync(() =>
        {
            writer.Append(data);
            output.ScrollToEnd();
        });
        proc.OnExit += (_, code) => Dispatcher.InvokeAsync(() =>
        {
            dot.Fill = code == 0 ? OkBrush : FailBrush;
            exitText.Text = $"({code})";
            exitText.Visibility = Visibility.Visible;
            stopButton.Visibility = Visibility.Collapsed;
            UpdateSessionInfo();
        });

        try
        {
            proc.Start();
            UpdateSessionInfo();
        }
        catch (Exception ex)
        {
            _sessions.Remove(proc.SessionId);
            SessionTabs.Items.Remove(tab);
            proc.Dispose();
            UpdateEmptyHint();
            ShowStatus($"Không chạy được: {ex.Message}", isError: true);
        }
    }

    private TabItem BuildSessionTab(string target, string id,
        out RichTextBox output, out AnsiWriter writer,
        out System.Windows.Shapes.Ellipse dot, out TextBlock exitText, out Button stopButton, out TextBox input)
    {
        dot = new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Fill = RunningBrush, VerticalAlignment = System.Windows.VerticalAlignment.Center };

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(dot);
        header.Children.Add(new TextBlock
        {
            Text = $"{target} #{id}", FontFamily = new FontFamily("Consolas"),
            FontSize = 11, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center,
        });
        exitText = new TextBlock
        {
            FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
            Margin = new Thickness(4, 0, 0, 0), Visibility = Visibility.Collapsed,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        };
        header.Children.Add(exitText);

        var sessionId = id;
        stopButton = new Button
        {
            Content = "■", FontSize = 10, Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(6, 0, 0, 0),
            ToolTip = "Kill process", Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand, VerticalAlignment = System.Windows.VerticalAlignment.Center,
        };
        stopButton.Click += (_, _) => KillSession(sessionId);
        header.Children.Add(stopButton);

        var closeButton = new Button
        {
            Content = "✕", FontSize = 10, Padding = new Thickness(4, 0, 4, 0),
            ToolTip = "Đóng tab", Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand, VerticalAlignment = System.Windows.VerticalAlignment.Center,
        };
        closeButton.Click += (_, _) => CloseTab(sessionId);
        header.Children.Add(closeButton);

        output = new RichTextBox
        {
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Background = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Document = new FlowDocument { PagePadding = new Thickness(0) },
        };
        writer = new AnsiWriter(output.Document);

        var w = writer;
        var procId = id;
        var inputBox = new TextBox
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Padding = new Thickness(8, 6, 8, 6),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
            Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
        };
        input = inputBox;
        var send = new Button
        {
            Content = "Gửi ⏎", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x08, 0x91, 0xB2)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
        };
        void SendInput()
        {
            if (!_sessions.TryGetValue(procId, out var s)) return;
            var text = inputBox.Text;
            if (string.IsNullOrEmpty(text)) return;
            inputBox.Clear();
            s.Writer.AppendEcho("> " + text);
            s.Output.ScrollToEnd();
            s.Proc.WriteInput(text + "\n");
        }
        send.Click += (_, _) => SendInput();
        inputBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { SendInput(); e.Handled = true; } };

        var inputRow = new DockPanel
        {
            LastChildFill = true, Margin = new Thickness(0, 8, 0, 0),
        };
        DockPanel.SetDock(send, Dock.Right);
        inputRow.Children.Add(send);
        inputRow.Children.Add(inputBox);

        var body = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(inputRow, Dock.Bottom);
        body.Children.Add(inputRow);
        body.Children.Add(output);

        var container = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8),
            Margin = new Thickness(12),
            Child = body,
        };

        return new TabItem { Header = header, Content = container, Tag = id };
    }

    private void KillSession(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var s))
        {
            s.Proc.Kill();
            s.Dot.Fill = KilledBrush;
            s.ExitText.Text = "(killed)";
            s.ExitText.Visibility = Visibility.Visible;
            s.StopButton.Visibility = Visibility.Collapsed;
            UpdateSessionInfo();
        }
    }

    private void CloseTab(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var s)) return;
        try { s.Proc.Kill(); } catch { }
        s.Proc.Dispose();
        _sessions.Remove(sessionId);
        SessionTabs.Items.Remove(s.Tab);
        UpdateEmptyHint();
        UpdateSessionInfo();
    }

    private void SessionTabs_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSessionInfo();

    private void UpdateEmptyHint() =>
        EmptyHint.Visibility = SessionTabs.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void UpdateSessionInfo()
    {
        string? activeId = (SessionTabs.SelectedItem as TabItem)?.Tag as string;
        if (activeId is not null && _sessions.TryGetValue(activeId, out var s))
        {
            var st = s.Proc.IsRunning ? "running" : $"exit {s.Proc.ExitCode}";
            SessionInfoText.Text = $"session #{s.Id} · {s.Proc.Target} · {st}";
        }
        else
        {
            SessionInfoText.Text = _sessions.Count == 0 ? string.Empty : $"{_sessions.Count} sessions";
        }
    }

    // ---------- Ghim Open-With ----------
    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (FileAssociation.IsRegistered())
                FileAssociation.Unregister();
            else
                FileAssociation.Register();
            RefreshPinButton();
        }
        catch (Exception ex)
        {
            ShowStatus($"Ghim Open-With thất bại: {ex.Message}", isError: true);
        }
    }

    private void RefreshPinButton()
    {
        bool on = FileAssociation.IsRegistered();
        PinButton.Content = on ? "✔ Đã ghim Open-With" : "📌 Ghim Open-With";
        PinButton.ToolTip = on
            ? "Đã ghim: double-click Makefile sẽ mở bằng app. Bấm để gỡ."
            : "Ghim app vào Open-With của file Makefile (double-click mở bằng app)";
    }

    private void ShowStatus(string msg, bool isError)
    {
        StatusText.Text = msg;
        StatusText.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26))
            : new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
        _statusTimer.Stop();
        _statusTimer.Start();
    }
}
