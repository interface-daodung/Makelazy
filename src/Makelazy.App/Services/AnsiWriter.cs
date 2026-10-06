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

    private static Brush B(byte r, byte g, byte b) =>
        new SolidColorBrush(Color.FromRgb(r, g, b));

    // Palette sáng (nền trắng) / tối (nền #0D1117)
    private static readonly Brush LightDefault = B(0x1F, 0x23, 0x28);
    private static readonly Brush LightMuted = B(0x6E, 0x77, 0x81);
    private static readonly Dictionary<int, Brush> LightFg = new()
    {
        [30] = B(0x24, 0x29, 0x2F), [31] = B(0xCF, 0x22, 0x2E),
        [32] = B(0x11, 0x63, 0x29), [33] = B(0x9A, 0x67, 0x00),
        [34] = B(0x09, 0x69, 0xDA), [35] = B(0x82, 0x50, 0xDF),
        [36] = B(0x1B, 0x7C, 0x83), [37] = LightMuted,
        [90] = B(0x57, 0x60, 0x6A), [91] = B(0xA4, 0x0E, 0x26),
        [92] = B(0x1A, 0x7F, 0x37), [93] = B(0xBF, 0x87, 0x00),
        [94] = B(0x21, 0x8B, 0xFF), [95] = B(0xA4, 0x75, 0xF9),
        [96] = B(0x31, 0x92, 0xAA), [97] = B(0x8C, 0x95, 0x9F),
    };
    private static readonly Dictionary<int, Brush> LightBg = new()
    {
        [40] = B(0x24, 0x29, 0x2F), [41] = B(0xFF, 0xEB, 0xE9),
        [42] = B(0xD8, 0xF3, 0xDC), [43] = B(0xFF, 0xF5, 0xCC),
        [44] = B(0xDD, 0xF4, 0xFF), [45] = B(0xF1, 0xE8, 0xFF),
        [46] = B(0xD5, 0xF1, 0xF3), [47] = B(0xEA, 0xEE, 0xF2),
    };
    private static readonly Brush DarkDefault = B(0xE6, 0xED, 0xF3);
    private static readonly Brush DarkMuted = B(0x8B, 0x94, 0x9E);
    private static readonly Dictionary<int, Brush> DarkFg = new()
    {
        [30] = B(0x48, 0x4F, 0x58), [31] = B(0xFF, 0x7B, 0x72),
        [32] = B(0x3F, 0xB9, 0x50), [33] = B(0xD2, 0x99, 0x22),
        [34] = B(0x58, 0xA6, 0xFF), [35] = B(0xBC, 0x8C, 0xFF),
        [36] = B(0x39, 0xC5, 0xCF), [37] = B(0xB1, 0xBA, 0xC4),
        [90] = B(0x6E, 0x76, 0x81), [91] = B(0xFF, 0xA1, 0x98),
        [92] = B(0x56, 0xD3, 0x64), [93] = B(0xE3, 0xB3, 0x41),
        [94] = B(0x79, 0xC0, 0xFF), [95] = B(0xD2, 0xA8, 0xFF),
        [96] = B(0x56, 0xD4, 0xDD), [97] = B(0xF0, 0xF6, 0xFC),
    };
    private static readonly Dictionary<int, Brush> DarkBg = new()
    {
        [40] = B(0x16, 0x1B, 0x22), [41] = B(0x5C, 0x22, 0x22),
        [42] = B(0x1F, 0x4D, 0x2E), [43] = B(0x5C, 0x4A, 0x1E),
        [44] = B(0x1E, 0x3A, 0x5C), [45] = B(0x3D, 0x2A, 0x5C),
        [46] = B(0x1E, 0x4A, 0x4E), [47] = B(0x30, 0x36, 0x3D),
    };

    private readonly FlowDocument _doc;
    private readonly StringBuilder _buf = new();
    private readonly List<Run> _lineRuns = new();
    private Brush _fg = LightDefault;
    private Brush _muted = LightMuted;
    private Brush _bg = Brushes.Transparent;
    private Dictionary<int, Brush> _fgMap = LightFg;
    private Dictionary<int, Brush> _bgMap = LightBg;
    private bool _bold;
    private bool _dark;

    public AnsiWriter(FlowDocument doc) => _doc = doc;

    /// <summary>Đổi palette theo theme. Output cũ giữ màu cũ, output mới dùng palette mới.</summary>
    public void ApplyTheme(bool dark)
    {
        _dark = dark;
        _fg = dark ? DarkDefault : LightDefault;
        _muted = dark ? DarkMuted : LightMuted;
        _fgMap = dark ? DarkFg : LightFg;
        _bgMap = dark ? DarkBg : LightBg;
        _bg = Brushes.Transparent;
        _bold = false;
    }

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
        var r = new Run(text.TrimEnd('\r', '\n')) { Foreground = _muted };
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
        _fg = _dark ? DarkDefault : LightDefault;
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
            else if (code == 39) _fg = _dark ? DarkDefault : LightDefault;
            else if (code == 49) _bg = Brushes.Transparent;
            else if (_fgMap.TryGetValue(code, out var f)) _fg = f;
            else if (_bgMap.TryGetValue(code, out var b)) _bg = b;
            else if (code >= 100 && code <= 107 && _bgMap.TryGetValue(code - 60, out var bb)) _bg = bb;
        }
    }
}
