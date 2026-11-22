using System.Linq;

namespace CatPet.Core;

/// <summary>
/// 动画索引：<c>类型 → 名字集合</c>、<c>名字 → 段落 → 动画列表</c>。
/// 与 VPet 的 <c>GraphCore</c> 同构：同名动画可以有多个"状态变体"，按需回退。
/// </summary>
public sealed class GraphBook
{
    private readonly Dictionary<GraphType, List<string>> _names = new();
    private readonly Dictionary<string, Dictionary<AnimatType, List<FrameAnimation>>> _list =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<GraphType, List<string>> Names => _names;

    public int Count { get; private set; }

    public void Add(FrameAnimation anim)
    {
        var info = anim.Info;

        if (info.Type != GraphType.Common)
        {
            if (!_names.TryGetValue(info.Type, out var names))
                _names[info.Type] = names = new List<string>();
            if (!names.Contains(info.Name))
                names.Add(info.Name);
        }

        if (!_list.TryGetValue(info.Name, out var byAnimat))
            _list[info.Name] = byAnimat = new Dictionary<AnimatType, List<FrameAnimation>>();

        if (!byAnimat.TryGetValue(info.Animat, out var list))
            byAnimat[info.Animat] = list = new List<FrameAnimation>();

        list.Add(anim);
        Count++;
    }

    /// <summary>随机取某个类型下的一个动画名（VPet: 同类型随机播放）。</summary>
    public string? FindName(GraphType type)
    {
        if (!_names.TryGetValue(type, out var names) || names.Count == 0)
            return null;
        return names[Random.Shared.Next(names.Count)];
    }

    public bool Has(GraphType type) => _names.TryGetValue(type, out var n) && n.Count > 0;

    /// <summary>
    /// 查找动画：精确状态 → 相邻状态 → 任意非 Ill 变体（VPet 的回退链）。
    /// </summary>
    public FrameAnimation? Find(string? name, AnimatType animat, ModeType mode)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        if (!_list.TryGetValue(name, out var byAnimat) || !byAnimat.TryGetValue(animat, out var list) || list.Count == 0)
            return null;

        var exact = list.Where(x => x.Info.Mode == mode).ToList();
        if (exact.Count > 0)
            return exact[Random.Shared.Next(exact.Count)];

        if (mode == ModeType.Ill)
            return null; // 生病状态不回退，避免"装没事"

        var lower = (int)mode + 1;
        if (lower < 3)
        {
            var l = list.Where(x => (int)x.Info.Mode == lower).ToList();
            if (l.Count > 0) return l[Random.Shared.Next(l.Count)];
        }

        var upper = (int)mode - 1;
        if (upper >= 1)
        {
            var u = list.Where(x => (int)x.Info.Mode == upper).ToList();
            if (u.Count > 0) return u[Random.Shared.Next(u.Count)];
        }

        var any = list.Where(x => x.Info.Mode != ModeType.Ill).ToList();
        return any.Count > 0 ? any[Random.Shared.Next(any.Count)] : null;
    }

    public FrameAnimation? Find(GraphType type, AnimatType animat, ModeType mode)
        => Find(FindName(type), animat, mode);

    /// <summary>按类型找到"整段"动画（A→B→C），缺段返回 null。</summary>
    public (FrameAnimation? A, FrameAnimation? B, FrameAnimation? C, FrameAnimation? Single) FindSet(
        GraphType type, ModeType mode, string? name = null)
    {
        name ??= FindName(type);
        if (name is null)
            return (null, null, null, null);

        return (
            Find(name, AnimatType.A_Start, mode),
            Find(name, AnimatType.B_Loop, mode),
            Find(name, AnimatType.C_End, mode),
            Find(name, AnimatType.Single, mode));
    }

    public IEnumerable<FrameAnimation> All()
        => _list.Values.SelectMany(d => d.Values).SelectMany(l => l);
}
