using System.Text;
using System.Windows.Documents;
using System.Windows.Media;

namespace Makelazy.App.Services;

/// <summary>
/// Render output của process (có mã màu ANSI) vào FlowDocument của RichTextBox.
/// Hiểu SGR (màu/bold/reset), \r\n, \r (ghi đè dòng — cho progress bar), bỏ qua các escape khác.
/// Mỗi tab terminal giữ 1 instance (giữ màu/dòng dở khi data tới theo chunk).
/// </summary>
public sealed class AnsiWriter
{
    private const int MaxBlocks = 4000;

    private static readonly Brush DefaultFg = new SolidColorBrush(Color.FromRgb(0x1F, 0x23, 0x28));
    private static readonly Brush MutedFg = new SolidColorBrush(Color.FromRgb(0x6E, 0x77, 0x81));

    private static readonly Dictionary<int, Brush> FgMap = new()
    {
        [30] = new SolidColorBrush(Color.FromRgb(0x24, 0x29, 0x2F)),
        [31] = new SolidColorBrush(Color.FromRgb(0xCF, 0x22, 0x2E)),
        [32] = new SolidColorBrush(Color.FromRgb(0x11, 0x63, 0x29)),
        [33] = new SolidColorBrush(Color.FromRgb(0x9A, 0x67, 0x00)),
        [34] = new SolidColorBrush(Color.FromRgb(0x09, 0x69, 0xDA)),
        [35] = new SolidColorBrush(Color.FromRgb(0x82, 0x50, 0xDF)),
        [36] = new SolidColorBrush(Color.FromRgb(0x1B, 0x7C, 0x83)),
        [37] = MutedFg,
        [90] = new SolidColorBrush(Color.FromRgb(0x57, 0x60, 0x6A)),
        [91] = new SolidColorBrush(Color.FromRgb(0xA4, 0x0E, 0x26)),
        [92] = new SolidColorBrush(Color.FromRgb(0x1A, 0x7F, 0x37)),
        [93] = new SolidColorBrush(Color.FromRgb(0xBF, 0x87, 0x00)),
        [94] = new SolidColorBrush(Color.FromRgb(0x21, 0x8B, 0xFF)),
        [95] = new SolidColorBrush(Color.FromRgb(0xA4, 0x75, 0xF9)),
        [96] = new SolidColorBrush(Color.FromRgb(0x31, 0x92, 0xAA)),
        [97] = new SolidColorBrush(Color.FromRgb(0x8C, 0x95, 0x9F)),
    };

    private static readonly Dictionary<int, Brush> BgMap = new()
    {
        [40] = new SolidColorBrush(Color.FromRgb(0x24, 0x29, 0x2F)),
        [41] = new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0xE9)),
        [42] = new SolidColorBrush(Color.FromRgb(0xD8, 0xF3, 0xDC)),
        [43] = new SolidColorBrush(Color.FromRgb(0xFF, 0xF5, 0xCC)),
        [44] = new SolidColorBrush(Color.FromRgb(0xDD, 0xF4, 0xFF)),
        [45] = new SolidColorBrush(Color.FromRgb(0xF1, 0xE8, 0xFF)),
        [46] = new SolidColorBrush(Color.FromRgb(0xD5, 0xF1, 0xF3)),
        [47] = new SolidColorBrush(Color.FromRgb(0xEA, 0xEE, 0xF2)),
    };

    private readonly FlowDocument _doc;
    private readonly StringBuilder _buf = new();
    private readonly List<Run> _lineRuns = new();
    private Brush _fg = DefaultFg;
    private Brush _bg = Brushes.Transparent;
    private bool _bold;

    public AnsiWriter(FlowDocument doc) => _doc = doc;

    public void Append(string data)
    {
        int i = 0;
        while (i < data.Length)
        {
            char c = data[i];
            if (c == '\x1b')
            {
                i = SkipEscape(data, i);
                continue;
            }
            if (c == '\n')
            {
                FlushLine();
                i++;
                continue;
            }
            if (c == '\r')
            {
                // \r\n = xuống dòng; \r lẻ = về đầu dòng (progress bar)
                if (i + 1 < data.Length && data[i + 1] == '\n') { i++; continue; }
                FlushBuffer();
                _lineRuns.Clear();
                i++;
                continue;
            }
            if (c == '\t') { _buf.Append("    "); i++; continue; }
            if (!char.IsControl(c)) _buf.Append(c);
            i++;
        }
        FlushBuffer();
    }

    /// <summary>Gõ stdin: echo lại dòng đã gửi (màu xám) để người dùng thấy.</summary>
    public void AppendEcho(string text)
    {
        FlushBuffer();
        var r = new Run(text.TrimEnd('\r', '\n')) { Foreground = MutedFg };
        _lineRuns.Add(r);
        FlushLine();
    }

    public void Clear()
    {
        _doc.Blocks.Clear();
        _lineRuns.Clear();
        _buf.Clear();
        ResetStyle();
    }

    private void FlushLine()
    {
        FlushBuffer();
        var p = new Paragraph { Margin = new System.Windows.Thickness(0) };
        if (_lineRuns.Count == 0)
            p.Inlines.Add(new Run(string.Empty));
        else
            foreach (var r in _lineRuns) p.Inlines.Add(r);
        _lineRuns.Clear();
        _doc.Blocks.Add(p);
        while (_doc.Blocks.Count > MaxBlocks)
            _doc.Blocks.Remove(_doc.Blocks.FirstBlock);
    }

    private void FlushBuffer()
    {
        if (_buf.Length == 0) return;
        var r = new Run(_buf.ToString()) { Foreground = _fg, Background = _bg };
        if (_bold) r.FontWeight = System.Windows.FontWeights.Bold;
        _lineRuns.Add(r);
        _buf.Clear();
    }

    private void ResetStyle()
    {
        _fg = DefaultFg;
        _bg = Brushes.Transparent;
        _bold = false;
    }

    private int SkipEscape(string s, int i)
    {
        // i đang ở ESC
        if (i + 1 >= s.Length) return s.Length;
        char n = s[i + 1];
        if (n == '[')
        {
            int j = i + 2;
            while (j < s.Length && !(s[j] >= '@' && s[j] <= '~')) j++;
            if (j < s.Length)
            {
                if (s[j] == 'm') ApplySgr(s.Substring(i + 2, j - (i + 2)));
                return j + 1;
            }
            return s.Length;
        }
        if (n == ']')
        {
            // OSC ... BEL hoặc ESC\
            int j = s.IndexOf('\x07', i + 2);
            int k = s.IndexOf("\x1b\\", i + 2, StringComparison.Ordinal);
            int end = j >= 0 && k >= 0 ? Math.Min(j, k) : Math.Max(j, k);
            return end >= 0 ? end + 1 : s.Length;
        }
        if ((n == '(' || n == ')' || n == '#' || n == '=' || n == '>') && i + 2 < s.Length)
            return i + 3;
        return i + 2;
    }

    private void ApplySgr(string body)
    {
        FlushBuffer();
        if (string.IsNullOrEmpty(body))
        {
            ResetStyle();
            return;
        }
        foreach (var part in body.Split(';'))
        {
            if (!int.TryParse(part, out int code)) continue;
            if (code == 0) ResetStyle();
            else if (code == 1) _bold = true;
            else if (code == 22) _bold = false;
            else if (code == 39) _fg = DefaultFg;
            else if (code == 49) _bg = Brushes.Transparent;
            else if (FgMap.TryGetValue(code, out var f)) _fg = f;
            else if (BgMap.TryGetValue(code, out var b)) _bg = b;
            else if (code >= 100 && code <= 107 && BgMap.TryGetValue(code - 60, out var bb)) _bg = bb;
        }
    }
}
