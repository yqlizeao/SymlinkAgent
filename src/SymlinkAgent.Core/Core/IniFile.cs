using System.Text;

namespace SymlinkAgent.Core;

/// <summary>
/// 极简 INI 读写(零依赖)。支持:节 [name]、key=value、重复 key(多值)、; 或 # 注释。
/// 写入时保持节与行的添加顺序;不保留注释(每次按模型重新生成)。
/// 值不做转义 —— 本工具只存路径/类型,不含换行(Windows 路径不含 '|' 等非法字符,用作分隔安全)。
/// </summary>
public sealed class IniFile
{
    public sealed class Section
    {
        public string Name { get; }
        public List<KeyValuePair<string, string>> Entries { get; } = new();
        public Section(string name) => Name = name;

        /// <summary>取首个同名 key 的值;无则返回 null。</summary>
        public string? First(string key) =>
            Entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

        /// <summary>取所有同名 key 的值(多值)。</summary>
        public IEnumerable<string> All(string key) =>
            Entries.Where(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)).Select(e => e.Value);

        public void Add(string key, string value) => Entries.Add(new(key, value));
    }

    private readonly List<Section> _sections = new();

    public IEnumerable<Section> Sections => _sections;

    public Section? Get(string name) =>
        _sections.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    public Section GetOrAdd(string name)
    {
        Section? s = Get(name);
        if (s is null) { s = new Section(name); _sections.Add(s); }
        return s;
    }

    public static IniFile Parse(string text)
    {
        var ini = new IniFile();
        Section? current = null;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;

            if (line[0] == '[' && line[^1] == ']')
            {
                current = ini.GetOrAdd(line[1..^1].Trim());
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq < 0 || current is null) continue;
            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();
            current.Add(key, value);
        }
        return ini;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (Section s in _sections)
        {
            sb.Append('[').Append(s.Name).Append("]\n");
            foreach (var e in s.Entries)
                sb.Append(e.Key).Append('=').Append(e.Value).Append('\n');
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
