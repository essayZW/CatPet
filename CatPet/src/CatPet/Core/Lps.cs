using System.Globalization;
using System.IO;
using System.Text;

namespace CatPet.Core;

/// <summary>
/// LinePutScript 的一行：<c>name#info:sub#value:sub2#value2:</c>
/// VPet 的 MOD / 形象 / 动画声明全部用这种格式。
/// </summary>
public sealed class LpsLine
{
    public string Name { get; init; } = "";
    public string Info { get; init; } = "";

    private readonly Dictionary<string, string> _subs = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> Subs => _subs;

    public void AddSub(string key, string value) => _subs[key] = value;

    public bool Has(string key) => _subs.ContainsKey(key);

    public string Get(string key, string def = "") => _subs.TryGetValue(key, out var v) ? v : def;

    public int GetInt(string key, int def = 0)
        => int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;

    public double GetDouble(string key, double def = 0)
        => double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;

    public bool GetBool(string key)
    {
        var s = Get(key).Trim().ToLowerInvariant();
        return s is "true" or "1" or "yes" or "t";
    }

    public override string ToString() => $"{Name}#{Info}";
}

/// <summary>
/// 极简 LPS 解析器（只实现 VPet 用到的最小子集）。
/// 语法要点：<b>一行文本 = 一个 Line</b>，行内 <c>name#info:sub#info:sub2#info2:</c>，
/// 段之间用 <c>|</c> 分隔（实现上把 '|' 当作"段结束"）。
/// 以 <c>//</c> 开头的是注释行；行首是 <c>|</c> 或 <c>:</c> 的是上一行的续行。
/// </summary>
public static class Lps
{
    public static List<LpsLine> ParseFile(string path) => Parse(File.ReadAllText(path, Encoding.UTF8));

    public static List<LpsLine> Parse(string text)
    {
        var result = new List<LpsLine>();
        if (text.Length > 0 && text[0] == '\uFEFF')
            text = text[1..];

        var buffer = new System.Text.StringBuilder();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0)
                continue;

            // 续行：以 '|' 或 ':' 开头
            if ((line[0] == '|' || line[0] == ':') && buffer.Length > 0)
            {
                buffer.Append(line);
                continue;
            }

            if (buffer.Length > 0)
            {
                AddLine(result, buffer.ToString());
                buffer.Clear();
            }

            if (line.StartsWith("//"))
                continue;

            buffer.Append(line);
        }

        if (buffer.Length > 0)
            AddLine(result, buffer.ToString());

        return result;
    }

    private static void AddLine(List<LpsLine> result, string text)
    {
        // '|' 结束一个段（line 的第一个段是 name#info，其余是 sub）
        var parts = text.Split('|');
        var head = parts[0].Trim().TrimEnd(':');
        var hash = head.IndexOf('#');
        var name = (hash >= 0 ? head[..hash] : head).Trim();
        var info = (hash >= 0 ? head[(hash + 1)..] : "").Trim();
        if (name.Length == 0)
            return;

        var line = new LpsLine { Name = name, Info = info };

        for (var i = 1; i < parts.Length; i++)
        {
            var s = parts[i].Trim().TrimEnd(':').Trim();
            if (s.Length == 0)
                continue;
            var h = s.IndexOf('#');
            if (h > 0)
                line.AddSub(s[..h].Trim(), s[(h + 1)..].Trim());
            else
                line.AddSub(s, "");
        }

        result.Add(line);
    }
}
