using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;

namespace AIVoxelStudio.Core.Mc;

// 从用户本地 .minecraft 提取方块模型/纹理，烘焙成统一图集与可渲染面表
public sealed class McAssets
{
    private const int TileSize = 16;
    private const int TilePitch = 18;
    public const float UnitsPerBlock = 16f;
    private const float Full = 16f;
    private const int CacheVersion = 2;

    private static readonly Color GrassTint = new(0.5686f, 0.7412f, 0.3490f);
    private static readonly Color FoliageTint = new(0.4627f, 0.6784f, 0.2588f);
    private static readonly Color WaterTint = new(0.2471f, 0.4627f, 0.8941f);

    // 原版靠 biome tintindex 上色，这里统一烘焙平原色调（不依赖群系）
    private static readonly Dictionary<string, Color> Tints = new(StringComparer.Ordinal)
    {
        ["block/grass_block_top"] = GrassTint,
        ["block/grass_block_side_overlay"] = GrassTint,
        ["block/short_grass"] = GrassTint,
        ["block/grass"] = GrassTint,
        ["block/tall_grass_top"] = GrassTint,
        ["block/tall_grass_bottom"] = GrassTint,
        ["block/fern"] = GrassTint,
        ["block/large_fern_top"] = GrassTint,
        ["block/large_fern_bottom"] = GrassTint,
        ["block/vine"] = FoliageTint,
        ["block/lily_pad"] = FoliageTint,
        ["block/oak_leaves"] = FoliageTint,
        ["block/jungle_leaves"] = FoliageTint,
        ["block/acacia_leaves"] = FoliageTint,
        ["block/dark_oak_leaves"] = FoliageTint,
        ["block/mangrove_leaves"] = FoliageTint,
        ["block/water_still"] = WaterTint,
        ["block/water_flow"] = WaterTint,
    };

    private static readonly string[] Directions = { "north", "south", "east", "west", "up", "down" };

    private readonly Dictionary<string, JsonObject> _modelJson = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ResolvedModel> _resolved = new(StringComparer.Ordinal);
    private readonly Dictionary<string, McBlockDef> _blockById = new(StringComparer.Ordinal);
    private readonly List<McBlockDef> _blocks = new();
    private readonly Dictionary<string, string> _lang = new(StringComparer.Ordinal);
    private readonly Dictionary<string, McTextureInfo> _textures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, McBakedState> _baked = new(StringComparer.Ordinal);
    private readonly Dictionary<string, McBlockState> _states = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Texture2D> _icons = new(StringComparer.Ordinal);

    private Image _atlasImage;
    private int _atlasSize;
    private int _fallbackX;
    private int _fallbackY;
    private int _jarTicks;
    private string _cacheDir;

    private McAssets() { }

    public string VersionKey { get; private set; }
    public string VersionName { get; private set; }
    public int DataVersion { get; private set; }
    public Texture2D Atlas { get; private set; }
    public IReadOnlyList<McBlockDef> Blocks => _blocks;

    public static McAssets Load(string mcRoot, string version)
    {
        var root = McAssetsLocator.NormalizeRoot(mcRoot);
        var jarPath = McAssetsLocator.VersionJar(root, version);
        if (string.IsNullOrEmpty(jarPath))
            throw new FileNotFoundException("未找到我的世界版本 jar，请检查设置中的目录与版本");

        var assets = new McAssets
        {
            VersionKey = version,
            _jarTicks = (int)(File.GetLastWriteTimeUtc(jarPath).Ticks & 0x7FFFFFFF),
            _cacheDir = ProjectSettings.GlobalizePath($"user://mc_assets/{Sanitize(version)}"),
        };

        using var zip = ZipFile.OpenRead(jarPath);
        assets.ReadVersion(zip);
        assets.ReadLang(zip);

        if (!assets.TryLoadAtlasCache())
        {
            assets.BuildAtlas(zip);
            assets.SaveAtlasCache();
        }

        assets.ReadModels(zip);
        assets.ReadBlockstates(zip);
        return assets;
    }

    public bool IsKnown(string id) => _blockById.ContainsKey(McBlockState.NormalizeId(id));

    public string DisplayName(string id)
    {
        id = McBlockState.NormalizeId(id);
        if (_lang.TryGetValue("block." + id.Replace(':', '.'), out var name)) return name;
        if (_blockById.TryGetValue(id, out var def)) return def.Name;
        return id;
    }

    // 用图集里模型正面（优先顶面）的 UV 区域作为方块图标
    public Texture2D Icon(McBlockState state)
    {
        if (state == null || state.IsAir || Atlas == null) return null;
        if (_icons.TryGetValue(state.FullString, out var cached)) return cached;

        Texture2D icon = null;
        McFace? face = null;
        foreach (var candidate in Baked(state).Faces)
        {
            if (candidate.Normal.Y > 0.9f)
            {
                face = candidate;
                break;
            }
            face ??= candidate;
        }

        if (face is { } chosen)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var uv in chosen.Uvs)
            {
                min = new Vector2(Mathf.Min(min.X, uv.X), Mathf.Min(min.Y, uv.Y));
                max = new Vector2(Mathf.Max(max.X, uv.X), Mathf.Max(max.Y, uv.Y));
            }

            var size = (max - min) * _atlasSize;
            if (size.X > 1f && size.Y > 1f)
            {
                icon = new AtlasTexture
                {
                    Atlas = Atlas,
                    Region = new Rect2(min * _atlasSize, size),
                };
            }
        }

        _icons[state.FullString] = icon;
        return icon;
    }

    public McBlockState DefaultState(string id) => ResolveState(id, null);

    // 严格解析：属性名与取值都必须存在于素材表；供 AI 工具与玩家写入前校验
    public bool TryResolveState(
        string id, IReadOnlyDictionary<string, string> properties,
        out McBlockState state, out string error)
    {
        state = null;
        error = null;
        id = McBlockState.NormalizeId(id);
        if (id == McBlockState.AirId)
        {
            state = McBlockState.Air;
            return true;
        }

        if (!_blockById.TryGetValue(id, out var def))
        {
            error = $"未知方块 {id}，请先调用 ListBlocks 搜索";
            return false;
        }

        if (properties != null)
        {
            foreach (var pair in properties)
            {
                if (!def.PropertyValues.TryGetValue(pair.Key, out var allowed))
                {
                    error = def.PropertyValues.Count > 0
                        ? $"{id} 没有属性 {pair.Key}，可用属性：{string.Join("|", def.PropertyValues.Keys.OrderBy(k => k, StringComparer.Ordinal))}"
                        : $"{id} 没有可配置属性";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(pair.Value)) continue;
                if (!allowed.Any(v => string.Equals(v, pair.Value, StringComparison.OrdinalIgnoreCase)))
                {
                    error = $"{id} 的 {pair.Key}={pair.Value} 非法，可选：{string.Join("|", allowed)}";
                    return false;
                }
            }
        }

        state = ResolveState(id, properties);
        return state != null;
    }

    public McBlockState ResolveState(string id, IReadOnlyDictionary<string, string> properties)
    {
        id = McBlockState.NormalizeId(id);
        if (id == McBlockState.AirId) return McBlockState.Air;
        if (!_blockById.TryGetValue(id, out var def)) return null;

        var values = new List<KeyValuePair<string, string>>();
        foreach (var pair in def.PropertyValues)
        {
            string value = def.Defaults[pair.Key];
            if (properties != null && properties.TryGetValue(pair.Key, out var provided) &&
                !string.IsNullOrWhiteSpace(provided))
                value = CanonicalValue(pair.Value, provided);
            values.Add(new KeyValuePair<string, string>(pair.Key, value));
        }

        if (properties != null)
        {
            foreach (var key in properties.Keys)
                if (!def.PropertyValues.ContainsKey(key)) return null;
        }

        var state = new McBlockState(id, values);
        if (_states.TryGetValue(state.FullString, out var existing)) return existing;
        _states[state.FullString] = state;
        ApplyStateFlags(state);
        return state;
    }

    public McBlockState ResolveSerialized(string text)
    {
        var parsed = McBlockState.Parse(text);
        if (parsed.IsAir) return McBlockState.Air;

        if (_blockById.TryGetValue(parsed.Id, out _))
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in parsed.Properties) dict[pair.Key] = pair.Value;
            var resolved = ResolveState(parsed.Id, dict);
            if (resolved != null) return resolved;
        }

        if (_states.TryGetValue(parsed.FullString, out var existing)) return existing;
        _states[parsed.FullString] = parsed;
        ApplyStateFlags(parsed);
        return parsed;
    }

    public McBakedState Baked(McBlockState state)
    {
        if (_baked.TryGetValue(state.FullString, out var cached)) return cached;

        var result = new McBakedState();
        if (!state.IsAir)
        {
            _blockById.TryGetValue(state.Id, out var def);
            if (def != null && def.IsFluid)
            {
                BuildFluid(def, result);
            }
            else if (def != null)
            {
                var used = new List<string>();

                if (def.Variants.Count > 0)
                {
                    McVariant chosen = null;
                    foreach (var variant in def.Variants)
                    {
                        if (!Matches(variant, state)) continue;
                        chosen = variant;
                        break;
                    }
                    chosen ??= def.Variants[0];
                    foreach (var model in chosen.Models) AppendModel(result.Faces, model, def, used);
                }

                foreach (var part in def.Multipart)
                {
                    if (!Matches(part.When, state)) continue;
                    foreach (var model in part.Models) AppendModel(result.Faces, model, def, used);
                }

                result.IsFullCube = used.Count > 0;
                result.IsCross = used.Count > 0;
                foreach (var model in used)
                {
                    var resolved = ResolveModel(model);
                    if (resolved == null || !resolved.IsFullCube) result.IsFullCube = false;
                    if (resolved == null || !resolved.IsCross) result.IsCross = false;
                }
            }
        }

        _baked[state.FullString] = result;
        return result;
    }

    private void ApplyStateFlags(McBlockState state)
    {
        var baked = Baked(state);
        state.IsFluid = baked.IsFluid;
        state.IsCross = baked.IsCross;
        state.IsSolid = !state.IsAir && !baked.IsFluid && !baked.IsCross;
        state.HasCutoutFaces = false;
        foreach (var face in baked.Faces)
        {
            if (face.Layer == 1)
            {
                state.HasCutoutFaces = true;
                break;
            }
        }
        _blockById.TryGetValue(state.Id, out var def);
        state.IsOpaqueCube = baked.IsFullCube && !state.IsFluid && !state.IsCross &&
                             !state.HasCutoutFaces && !(def?.ForceTranslucent ?? false);
    }

    private void ReadVersion(ZipArchive zip)
    {
        VersionName = VersionKey;
        var entry = zip.GetEntry("version.json");
        if (entry == null) return;
        try
        {
            var json = JsonNode.Parse(ReadString(entry)) as JsonObject;
            if (json == null) return;
            if (json["id"] is JsonValue id && id.TryGetValue<string>(out var name)) VersionName = name;
            if (json["world_version"] is JsonValue world && world.TryGetValue<int>(out int dataVersion)) DataVersion = dataVersion;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"读取 version.json 失败: {ex.Message}");
        }
    }

    private void ReadLang(ZipArchive zip)
    {
        foreach (var lang in new[] { "zh_cn", "en_us" })
        {
            var entry = zip.GetEntry($"assets/minecraft/lang/{lang}.json");
            if (entry == null) continue;
            try
            {
                var json = JsonNode.Parse(ReadString(entry)) as JsonObject;
                if (json == null) continue;
                foreach (var pair in json)
                {
                    if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text))
                        _lang[pair.Key] = text;
                }
                return;
            }
            catch (Exception ex)
            {
                GD.PushWarning($"读取语言文件 {lang} 失败: {ex.Message}");
            }
        }
    }

    private void ReadModels(ZipArchive zip)
    {
        const string prefix = "assets/minecraft/models/block/";
        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal) ||
                !entry.FullName.EndsWith(".json", StringComparison.Ordinal)) continue;

            var path = "block/" + entry.FullName[prefix.Length..^5];
            try
            {
                if (JsonNode.Parse(ReadString(entry)) is JsonObject json) _modelJson[path] = json;
            }
            catch
            {
                // 单个模型损坏不影响其它方块
            }
        }
    }

    private void ReadBlockstates(ZipArchive zip)
    {
        const string prefix = "assets/minecraft/blockstates/";
        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal) ||
                !entry.FullName.EndsWith(".json", StringComparison.Ordinal)) continue;

            string path = entry.FullName[prefix.Length..^5];
            string id = McBlockState.NormalizeId(path);
            JsonObject root;
            try
            {
                root = JsonNode.Parse(ReadString(entry)) as JsonObject;
            }
            catch
            {
                continue;
            }
            if (root == null) continue;

            var def = new McBlockDef
            {
                Id = id,
                Path = path,
                Name = DisplayName(id),
                IsFluid = id is "minecraft:water" or "minecraft:lava" or "minecraft:flowing_water" or "minecraft:flowing_lava",
                ForceTranslucent = id.Contains("water") || id.Contains("glass") || id.Contains("ice") ||
                                   id.Contains("slime") || id.Contains("honey"),
            };

            if (root["variants"] is JsonObject variants)
            {
                foreach (var pair in variants)
                {
                    var variant = new McVariant();
                    ParseVariantKey(pair.Key, variant.Conditions);
                    CollectProperties(variant.Conditions, def);
                    ParseModels(pair.Value, false, variant.Models);
                    if (variant.Models.Count > 0) def.Variants.Add(variant);
                }
            }

            if (root["multipart"] is JsonArray multipart)
            {
                foreach (var node in multipart)
                {
                    if (node is not JsonObject part) continue;
                    var mp = new McMultipart();
                    if (part["when"] is JsonObject when)
                    {
                        mp.When = ParseCondition(when);
                        CollectProperties(mp.When, def);
                    }
                    ParseModels(part["apply"], true, mp.Models);
                    if (mp.Models.Count > 0) def.Multipart.Add(mp);
                }
            }

            FinalizeProperties(def);
            _blockById[id] = def;
            _blocks.Add(def);
        }

        _blocks.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
    }

    private static void ParseVariantKey(string key, Dictionary<string, string[]> target)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        foreach (var token in key.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = token.IndexOf('=');
            if (eq <= 0) continue;
            target[token[..eq].Trim()] = token[(eq + 1)..].Trim().Split('|');
        }
    }

    private static void ParseModels(JsonNode node, bool all, List<McModelRef> target)
    {
        if (node is JsonObject single)
        {
            AddModelRef(single, target);
            return;
        }
        if (node is not JsonArray array) return;

        if (all)
        {
            foreach (var item in array)
                if (item is JsonObject obj) AddModelRef(obj, target);
        }
        else if (array.Count > 0 && array[0] is JsonObject first)
        {
            AddModelRef(first, target);
        }
    }

    private static void AddModelRef(JsonObject obj, List<McModelRef> target)
    {
        if (obj["model"] is not JsonValue modelValue || !modelValue.TryGetValue<string>(out var model)) return;
        target.Add(new McModelRef
        {
            Model = NormalizeModelPath(model),
            X = ReadInt(obj["x"]),
            Y = ReadInt(obj["y"]),
        });
    }

    private static McCondition ParseCondition(JsonObject when)
    {
        var condition = new McCondition();
        foreach (var pair in when)
        {
            if (pair.Key == "OR" && pair.Value is JsonArray or)
            {
                foreach (var item in or)
                    if (item is JsonObject obj) condition.Or.Add(ParseCondition(obj));
                continue;
            }
            if (pair.Key == "AND" && pair.Value is JsonArray and)
            {
                foreach (var item in and)
                    if (item is JsonObject obj) condition.And.Add(ParseCondition(obj));
                continue;
            }
            if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text))
                condition.Values[pair.Key] = text.Split('|');
        }
        return condition;
    }

    private static void CollectProperties(McCondition condition, McBlockDef def)
    {
        if (condition == null) return;
        foreach (var pair in condition.Values) AddPropertyValues(def, pair.Key, pair.Value);
        foreach (var child in condition.Or) CollectProperties(child, def);
        foreach (var child in condition.And) CollectProperties(child, def);
    }

    private static void CollectProperties(Dictionary<string, string[]> conditions, McBlockDef def)
    {
        foreach (var pair in conditions) AddPropertyValues(def, pair.Key, pair.Value);
    }

    private static void AddPropertyValues(McBlockDef def, string name, IEnumerable<string> values)
    {
        if (!def.PropertyValues.TryGetValue(name, out var list))
        {
            list = new List<string>();
            def.PropertyValues[name] = list;
        }
        foreach (var value in values)
            if (!string.IsNullOrEmpty(value) && !list.Contains(value)) list.Add(value);
    }

    // 属性取值大小写容错：命中时用素材表里的规范写法，未命中保持原样（存档兼容）
    private static string CanonicalValue(List<string> allowed, string provided)
    {
        foreach (var value in allowed)
            if (string.Equals(value, provided, StringComparison.OrdinalIgnoreCase)) return value;
        return provided;
    }

    private static void FinalizeProperties(McBlockDef def)
    {
        if (def.PropertyValues.Count == 0) return;

        foreach (var pair in def.PropertyValues)
        {
            var values = pair.Value;
            if (values.Contains("true") && !values.Contains("false")) values.Add("false");

            string chosen = null;
            foreach (var preferred in new[] { "false", "none", "north", "bottom", "straight", "y", "up", "0" })
            {
                if (values.Contains(preferred))
                {
                    chosen = preferred;
                    break;
                }
            }
            chosen ??= values[0];
            def.Defaults[pair.Key] = chosen;
        }
    }

    private bool Matches(McVariant variant, McBlockState state)
    {
        foreach (var pair in variant.Conditions)
        {
            string actual = state.GetProperty(pair.Key);
            if (actual == null || !pair.Value.Contains(actual)) return false;
        }
        return true;
    }

    private bool Matches(McCondition condition, McBlockState state)
    {
        if (condition == null) return true;

        foreach (var pair in condition.Values)
        {
            string actual = state.GetProperty(pair.Key);
            if (actual == null || !pair.Value.Contains(actual)) return false;
        }
        if (condition.Or.Count > 0 && !condition.Or.Any(child => Matches(child, state))) return false;
        if (condition.And.Count > 0 && !condition.And.All(child => Matches(child, state))) return false;
        return true;
    }

    private void AppendModel(List<McFace> faces, McModelRef reference, McBlockDef def, List<string> used)
    {
        var model = ResolveModel(reference.Model);
        if (model == null) return;
        if (!used.Contains(reference.Model)) used.Add(reference.Model);

        foreach (var element in model.Elements)
        {
            foreach (var pair in element.Faces)
            {
                string direction = pair.Key;
                if (Array.IndexOf(Directions, direction) < 0) continue;
                var faceDef = pair.Value;

                string texture = ResolveTexture(faceDef.Texture, model.Textures, 0);
                var tile = GetTile(texture);
                int layer = def.ForceTranslucent ? 2 : tile.HasAlpha ? 1 : 0;

                var corners = BuildCorners(direction, element.From, element.To);
                var uv = DefaultUv(direction, corners);
                if (faceDef.Uv != null) uv = MapUv(uv, faceDef.Uv);
                if (faceDef.Rotation != 0) uv = RotateUv(uv, faceDef.Rotation);
                for (int i = 0; i < 4; i++) uv[i] = AtlasUv(tile, uv[i]);

                for (int i = 0; i < 4; i++)
                {
                    var point = corners[i];
                    point = RotatePoint(point, element.Rotation);
                    point = RotateModelPoint(point, reference.X, reference.Y);
                    corners[i] = point;
                }

                var normal = DirVector(direction);
                normal = RotateVector(normal, element.Rotation);
                normal = RotateModelVector(normal, reference.X, reference.Y);

                string cull = faceDef.CullFace;
                if (cull != null)
                {
                    var cullVector = DirVector(cull);
                    cullVector = RotateVector(cullVector, element.Rotation);
                    cullVector = RotateModelVector(cullVector, reference.X, reference.Y);
                    cull = DirName(cullVector);
                }

                faces.Add(new McFace(corners, uv, normal, cull, layer));
            }
        }
    }

    private void BuildFluid(McBlockDef def, McBakedState result)
    {
        bool water = def.Id.Contains("water");
        string texture = water ? "block/water_still" : "block/lava_still";
        var tile = GetTile(texture);
        int layer = water ? 2 : 0;

        foreach (var direction in Directions)
        {
            var corners = BuildCorners(direction, new[] { 0f, 0f, 0f }, new[] { Full, Full, Full });
            var uv = DefaultUv(direction, corners);
            for (int i = 0; i < 4; i++) uv[i] = AtlasUv(tile, uv[i]);
            result.Faces.Add(new McFace(corners, uv, DirVector(direction), direction, layer));
        }

        result.IsFullCube = true;
        result.IsFluid = true;
    }

    private Vector3[] BuildCorners(string direction, float[] from, float[] to)
    {
        float x1 = from[0], y1 = from[1], z1 = from[2];
        float x2 = to[0], y2 = to[1], z2 = to[2];
        return direction switch
        {
            "down" => new[] { new Vector3(x1, y1, z1), new Vector3(x2, y1, z1), new Vector3(x2, y1, z2), new Vector3(x1, y1, z2) },
            "up" => new[] { new Vector3(x1, y2, z1), new Vector3(x1, y2, z2), new Vector3(x2, y2, z2), new Vector3(x2, y2, z1) },
            "north" => new[] { new Vector3(x2, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y2, z1), new Vector3(x2, y2, z1) },
            "south" => new[] { new Vector3(x1, y1, z2), new Vector3(x2, y1, z2), new Vector3(x2, y2, z2), new Vector3(x1, y2, z2) },
            "west" => new[] { new Vector3(x1, y1, z2), new Vector3(x1, y1, z1), new Vector3(x1, y2, z1), new Vector3(x1, y2, z2) },
            _ => new[] { new Vector3(x2, y1, z1), new Vector3(x2, y1, z2), new Vector3(x2, y2, z2), new Vector3(x2, y2, z1) },
        };
    }

    private static Vector2[] DefaultUv(string direction, Vector3[] corners)
    {
        var uv = new Vector2[4];
        for (int i = 0; i < 4; i++)
        {
            var p = corners[i];
            uv[i] = direction switch
            {
                "down" => new Vector2(p.X, p.Z),
                "up" => new Vector2(p.X, Full - p.Z),
                "north" => new Vector2(Full - p.X, Full - p.Y),
                "south" => new Vector2(p.X, Full - p.Y),
                "west" => new Vector2(p.Z, Full - p.Y),
                _ => new Vector2(Full - p.Z, Full - p.Y),
            };
        }
        return uv;
    }

    private static Vector2[] MapUv(Vector2[] uv, float[] rect)
    {
        float u1 = rect[0], v1 = rect[1], u2 = rect[2], v2 = rect[3];
        var result = new Vector2[4];
        for (int i = 0; i < 4; i++)
        {
            float t = uv[i].X / Full;
            float s = uv[i].Y / Full;
            result[i] = new Vector2(u1 + t * (u2 - u1), v1 + s * (v2 - v1));
        }
        return result;
    }

    private static Vector2[] RotateUv(Vector2[] uv, int rotation)
    {
        float u1 = float.MaxValue, v1 = float.MaxValue, u2 = float.MinValue, v2 = float.MinValue;
        foreach (var p in uv)
        {
            u1 = Mathf.Min(u1, p.X);
            v1 = Mathf.Min(v1, p.Y);
            u2 = Mathf.Max(u2, p.X);
            v2 = Mathf.Max(v2, p.Y);
        }
        float w = Mathf.Max(0.0001f, u2 - u1);
        float h = Mathf.Max(0.0001f, v2 - v1);

        var result = new Vector2[4];
        for (int i = 0; i < 4; i++)
        {
            float t = (uv[i].X - u1) / w;
            float s = (uv[i].Y - v1) / h;
            switch (rotation)
            {
                case 90: (t, s) = (s, 1f - t); break;
                case 180: (t, s) = (1f - t, 1f - s); break;
                case 270: (t, s) = (1f - s, t); break;
            }
            result[i] = new Vector2(u1 + t * w, v1 + s * h);
        }
        return result;
    }

    private Vector2 AtlasUv(McTextureInfo tile, Vector2 uv)
    {
        float u = tile.X + Mathf.Clamp(uv.X, 0f, Full - 0.001f);
        float v = tile.Y + Mathf.Clamp(uv.Y, 0f, Full - 0.001f);
        return new Vector2(u / _atlasSize, v / _atlasSize);
    }

    private McTextureInfo GetTile(string texture)
    {
        if (texture != null && _textures.TryGetValue(texture, out var tile)) return tile;
        return new McTextureInfo(_fallbackX, _fallbackY, false);
    }

    private ResolvedModel ResolveModel(string path)
    {
        path = NormalizeModelPath(path);
        if (_resolved.TryGetValue(path, out var cached)) return cached;

        var result = new ResolvedModel();
        if (!_modelJson.TryGetValue(path, out var json))
        {
            _resolved[path] = result;
            return result;
        }

        if (json["parent"] is JsonValue parentValue && parentValue.TryGetValue<string>(out var parent))
        {
            var parentModel = ResolveModel(parent);
            foreach (var pair in parentModel.Textures) result.Textures[pair.Key] = pair.Value;
            result.Elements = parentModel.Elements;
        }

        if (json["textures"] is JsonObject textures)
        {
            foreach (var pair in textures)
            {
                if (pair.Value is JsonValue value && value.TryGetValue<string>(out var texture))
                    result.Textures[pair.Key] = texture;
            }
        }

        if (json["elements"] is JsonArray elements) result.Elements = ParseElements(elements);

        result.IsFullCube = ComputeFullCube(result.Elements);
        result.IsCross = ComputeCross(result.Elements);
        _resolved[path] = result;
        return result;
    }

    private static List<McElement> ParseElements(JsonArray array)
    {
        var elements = new List<McElement>();
        foreach (var node in array)
        {
            if (node is not JsonObject obj) continue;
            if (obj["from"] is not JsonArray fromArray || obj["to"] is not JsonArray toArray) continue;

            var element = new McElement
            {
                From = ReadFloats(fromArray),
                To = ReadFloats(toArray),
                Rotation = ReadRotation(obj["rotation"]),
            };

            if (obj["faces"] is JsonObject faces)
            {
                foreach (var pair in faces)
                {
                    if (pair.Value is not JsonObject faceObj) continue;
                    string texture = null;
                    if (faceObj["texture"] is JsonValue tv && tv.TryGetValue<string>(out var t)) texture = t;
                    if (string.IsNullOrEmpty(texture)) continue;

                    element.Faces[pair.Key] = new McFaceDef
                    {
                        Texture = texture,
                        Uv = faceObj["uv"] is JsonArray uvArray ? ReadFloats(uvArray) : null,
                        CullFace = faceObj["cullface"] is JsonValue cv && cv.TryGetValue<string>(out var c) ? c : null,
                        Rotation = ReadInt(faceObj["rotation"]),
                    };
                }
            }

            if (element.Faces.Count > 0) elements.Add(element);
        }
        return elements;
    }

    private static Rotation3 ReadRotation(JsonNode node)
    {
        if (node is not JsonObject obj) return Rotation3.None;
        return new Rotation3
        {
            Origin = obj["origin"] is JsonArray origin ? ReadFloats(origin) : new[] { 8f, 8f, 8f },
            Axis = obj["axis"] is JsonValue axis && axis.TryGetValue<string>(out var a) ? a : "y",
            Angle = ReadFloat(obj["angle"]),
            Valid = true,
        };
    }

    private string ResolveTexture(string value, Dictionary<string, string> textures, int depth)
    {
        if (string.IsNullOrEmpty(value) || depth > 16) return null;
        if (value[0] == '#')
        {
            return textures.TryGetValue(value[1..], out var next)
                ? ResolveTexture(next, textures, depth + 1)
                : null;
        }
        value = value.Replace("minecraft:", "");
        return value.Contains('/') ? value : "block/" + value;
    }

    private static bool ComputeFullCube(List<McElement> elements)
    {
        if (elements.Count == 0) return false;

        float minX = Full, minY = Full, minZ = Full;
        float maxX = 0f, maxY = 0f, maxZ = 0f;
        foreach (var element in elements)
        {
            if (element.Rotation.Valid && !Mathf.IsZeroApprox(element.Rotation.Angle)) return false;
            minX = Mathf.Min(minX, element.From[0]);
            minY = Mathf.Min(minY, element.From[1]);
            minZ = Mathf.Min(minZ, element.From[2]);
            maxX = Mathf.Max(maxX, element.To[0]);
            maxY = Mathf.Max(maxY, element.To[1]);
            maxZ = Mathf.Max(maxZ, element.To[2]);
        }
        return minX <= 0.001f && minY <= 0.001f && minZ <= 0.001f &&
               maxX >= Full - 0.001f && maxY >= Full - 0.001f && maxZ >= Full - 0.001f;
    }

    private static bool ComputeCross(List<McElement> elements)
    {
        if (elements.Count == 0) return false;
        foreach (var element in elements)
        {
            float dx = Mathf.Abs(element.To[0] - element.From[0]);
            float dy = Mathf.Abs(element.To[1] - element.From[1]);
            float dz = Mathf.Abs(element.To[2] - element.From[2]);
            if (dx > 0.01f && dy > 0.01f && dz > 0.01f) return false;
        }
        return true;
    }

    private static Vector3 RotatePoint(Vector3 point, Rotation3 rotation)
    {
        if (!rotation.Valid || Mathf.IsZeroApprox(rotation.Angle)) return point;
        var origin = new Vector3(rotation.Origin[0], rotation.Origin[1], rotation.Origin[2]);
        return origin + RotateVector(point - origin, rotation);
    }

    private static Vector3 RotateVector(Vector3 value, Rotation3 rotation)
    {
        if (!rotation.Valid || Mathf.IsZeroApprox(rotation.Angle)) return value;
        float rad = Mathf.DegToRad(rotation.Angle);
        float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
        return rotation.Axis switch
        {
            "x" => new Vector3(value.X, value.Y * c - value.Z * s, value.Y * s + value.Z * c),
            "z" => new Vector3(value.X * c - value.Y * s, value.X * s + value.Y * c, value.Z),
            _ => new Vector3(value.X * c + value.Z * s, value.Y, -value.X * s + value.Z * c),
        };
    }

    private static Vector3 RotateModelPoint(Vector3 point, int rotX, int rotY)
    {
        var relative = point - new Vector3(8f, 8f, 8f);
        relative = RotateModelVector(relative, rotX, rotY);
        return relative + new Vector3(8f, 8f, 8f);
    }

    private static Vector3 RotateModelVector(Vector3 value, int rotX, int rotY)
    {
        if (rotX % 360 != 0)
        {
            float rad = Mathf.DegToRad(rotX % 360);
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            value = new Vector3(value.X, value.Y * c - value.Z * s, value.Y * s + value.Z * c);
        }

        switch (((rotY % 360) + 360) % 360)
        {
            case 90: value = new Vector3(-value.Z, value.Y, value.X); break;
            case 180: value = new Vector3(-value.X, value.Y, -value.Z); break;
            case 270: value = new Vector3(value.Z, value.Y, -value.X); break;
        }
        return value;
    }

    private static Vector3 DirVector(string direction) => direction switch
    {
        "down" => Vector3.Down,
        "up" => Vector3.Up,
        "north" => Vector3.Forward,
        "south" => Vector3.Back,
        "west" => Vector3.Left,
        _ => Vector3.Right,
    };

    private static string DirName(Vector3 vector)
    {
        if (Mathf.Abs(vector.X) >= Mathf.Abs(vector.Y) && Mathf.Abs(vector.X) >= Mathf.Abs(vector.Z))
            return vector.X >= 0 ? "east" : "west";
        if (Mathf.Abs(vector.Y) >= Mathf.Abs(vector.Z))
            return vector.Y >= 0 ? "up" : "down";
        return vector.Z >= 0 ? "south" : "north";
    }

    private static string NormalizeModelPath(string path)
    {
        path = path.Replace("minecraft:", "");
        if (path.StartsWith("models/", StringComparison.Ordinal)) path = path[7..];
        return path.Contains('/') ? path : "block/" + path;
    }

    private void BuildAtlas(ZipArchive zip)
    {
        const string prefix = "assets/minecraft/textures/";
        var images = new Dictionary<string, Image>(StringComparer.Ordinal);

        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal) ||
                !entry.FullName.Contains("/block/", StringComparison.Ordinal) ||
                !entry.FullName.EndsWith(".png", StringComparison.Ordinal)) continue;

            string name = entry.FullName[prefix.Length..^4];
            var image = LoadTexture(zip, entry);
            if (image != null) images[name] = image;
        }

        images["__fallback"] = CreateFallback();

        var names = images.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList();
        int grid = Mathf.CeilToInt(Mathf.Sqrt(names.Count));
        _atlasSize = Mathf.Max(TileSize, grid * TilePitch);
        _atlasImage = Image.CreateEmpty(_atlasSize, _atlasSize, false, Image.Format.Rgba8);
        _atlasImage.Fill(new Color(0f, 0f, 0f, 0f));

        for (int i = 0; i < names.Count; i++)
        {
            string name = names[i];
            int x = (i % grid) * TilePitch;
            int y = (i / grid) * TilePitch;
            var image = images[name];
            if (Tints.TryGetValue(name, out var tint)) TintImage(image, tint);
            _atlasImage.BlitRect(image, new Rect2I(0, 0, TileSize, TileSize), new Vector2I(x, y));

            bool hasAlpha = HasAlpha(image);
            if (name == "__fallback")
            {
                _fallbackX = x;
                _fallbackY = y;
            }
            else
            {
                _textures[name] = new McTextureInfo(x, y, hasAlpha);
            }
        }

        Atlas = ImageTexture.CreateFromImage(_atlasImage);
    }

    private Image LoadTexture(ZipArchive zip, ZipArchiveEntry entry)
    {
        try
        {
            var image = new Image();
            if (image.LoadPngFromBuffer(ReadBytes(entry)) != Error.Ok) return null;

            var metaEntry = zip.GetEntry(entry.FullName + ".mcmeta");
            if (metaEntry != null && image.GetHeight() > image.GetWidth() &&
                image.GetHeight() % image.GetWidth() == 0)
            {
                var meta = JsonNode.Parse(ReadString(metaEntry)) as JsonObject;
                if (meta?["animation"] != null)
                {
                    var frame = image.GetRegion(new Rect2I(0, 0, image.GetWidth(), image.GetWidth()));
                    image = frame;
                }
            }

            if (image.GetWidth() != TileSize || image.GetHeight() != TileSize)
                image.Resize(TileSize, TileSize, Image.Interpolation.Nearest);
            if (image.GetFormat() != Image.Format.Rgba8) image.Convert(Image.Format.Rgba8);
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static bool HasAlpha(Image image)
    {
        var data = image.GetData();
        for (int i = 3; i < data.Length; i += 4)
            if (data[i] < 250) return true;
        return false;
    }

    private static Image CreateFallback()
    {
        var image = Image.CreateEmpty(TileSize, TileSize, false, Image.Format.Rgba8);
        var magenta = new Color(1f, 0f, 1f);
        var black = new Color(0.08f, 0.08f, 0.08f);
        for (int y = 0; y < TileSize; y++)
        for (int x = 0; x < TileSize; x++)
            image.SetPixel(x, y, ((x / 4 + y / 4) % 2 == 0) ? magenta : black);
        return image;
    }

    private static void TintImage(Image image, Color tint)
    {
        for (int y = 0; y < image.GetHeight(); y++)
        for (int x = 0; x < image.GetWidth(); x++)
        {
            var pixel = image.GetPixel(x, y);
            image.SetPixel(x, y, new Color(pixel.R * tint.R, pixel.G * tint.G, pixel.B * tint.B, pixel.A));
        }
    }

    private bool TryLoadAtlasCache()
    {
        string metaPath = Path.Combine(_cacheDir, "atlas.json");
        string pngPath = Path.Combine(_cacheDir, "atlas.png");
        if (!File.Exists(metaPath) || !File.Exists(pngPath)) return false;

        try
        {
            var meta = JsonSerializer.Deserialize<AtlasMeta>(File.ReadAllText(metaPath));
            if (meta == null || meta.CacheVersion != CacheVersion || meta.JarTicks != _jarTicks ||
                meta.Textures == null) return false;

            var image = Image.LoadFromFile(pngPath);
            if (image == null) return false;
            if (image.GetFormat() != Image.Format.Rgba8) image.Convert(Image.Format.Rgba8);

            _atlasImage = image;
            _atlasSize = meta.Size;
            DataVersion = meta.DataVersion;
            VersionName = meta.VersionName;
            _fallbackX = meta.FallbackX;
            _fallbackY = meta.FallbackY;
            _textures.Clear();
            foreach (var texture in meta.Textures)
                _textures[texture.Name] = new McTextureInfo(texture.X, texture.Y, texture.HasAlpha);
            Atlas = ImageTexture.CreateFromImage(image);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void SaveAtlasCache()
    {
        if (_atlasImage == null) return;
        try
        {
            Directory.CreateDirectory(_cacheDir);
            _atlasImage.SavePng(Path.Combine(_cacheDir, "atlas.png"));

            var meta = new AtlasMeta
            {
                CacheVersion = CacheVersion,
                JarTicks = _jarTicks,
                Size = _atlasSize,
                DataVersion = DataVersion,
                VersionName = VersionName,
                FallbackX = _fallbackX,
                FallbackY = _fallbackY,
                Textures = _textures.Select(pair => new TextureMeta
                {
                    Name = pair.Key,
                    X = pair.Value.X,
                    Y = pair.Value.Y,
                    HasAlpha = pair.Value.HasAlpha,
                }).ToList(),
            };
            File.WriteAllText(Path.Combine(_cacheDir, "atlas.json"), JsonSerializer.Serialize(meta));
        }
        catch (Exception ex)
        {
            GD.PushWarning($"写入 MC 素材缓存失败: {ex.Message}");
        }
    }

    private static string ReadString(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static byte[] ReadBytes(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static int ReadInt(JsonNode node) =>
        node is JsonValue value && value.TryGetValue<int>(out int result) ? result : 0;

    private static float[] ReadFloats(JsonArray array)
    {
        var result = new float[array.Count];
        for (int i = 0; i < array.Count; i++) result[i] = ReadFloat(array[i]);
        return result;
    }

    private static float ReadFloat(JsonNode node)
    {
        if (node is not JsonValue value) return 0f;
        if (value.TryGetValue<float>(out float single)) return single;
        if (value.TryGetValue<double>(out double number)) return (float)number;
        if (value.TryGetValue<int>(out int integer)) return integer;
        return 0f;
    }

    private static string Sanitize(string text)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) text = text.Replace(c, '_');
        return text;
    }

    private readonly struct McTextureInfo
    {
        public readonly int X;
        public readonly int Y;
        public readonly bool HasAlpha;

        public McTextureInfo(int x, int y, bool hasAlpha)
        {
            X = x;
            Y = y;
            HasAlpha = hasAlpha;
        }
    }

    private sealed class ResolvedModel
    {
        public readonly Dictionary<string, string> Textures = new(StringComparer.Ordinal);
        public List<McElement> Elements = new();
        public bool IsFullCube;
        public bool IsCross;
    }

    private sealed class McElement
    {
        public float[] From;
        public float[] To;
        public Rotation3 Rotation;
        public readonly Dictionary<string, McFaceDef> Faces = new(StringComparer.Ordinal);
    }

    private sealed class McFaceDef
    {
        public string Texture;
        public float[] Uv;
        public string CullFace;
        public int Rotation;
    }

    private struct Rotation3
    {
        public static readonly Rotation3 None = new() { Valid = false };

        public float[] Origin;
        public string Axis;
        public float Angle;
        public bool Valid;
    }

    private sealed class AtlasMeta
    {
        public int CacheVersion { get; set; }
        public int JarTicks { get; set; }
        public int Size { get; set; }
        public int DataVersion { get; set; }
        public string VersionName { get; set; }
        public int FallbackX { get; set; }
        public int FallbackY { get; set; }
        public List<TextureMeta> Textures { get; set; }
    }

    private sealed class TextureMeta
    {
        public string Name { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public bool HasAlpha { get; set; }
    }
}
