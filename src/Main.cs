using System.Diagnostics;
using Godot;
using AIVoxelStudio.AI;
using AIVoxelStudio.AI.Tools;
using AIVoxelStudio.App;
using AIVoxelStudio.Core;
using AIVoxelStudio.Core.Abstractions;
using AIVoxelStudio.Core.Export;
using AIVoxelStudio.Core.Mc;
using AIVoxelStudio.Core.Profiles;
using AIVoxelStudio.Core.Selection;
using AIVoxelStudio.Player;
using AIVoxelStudio.Render;
using AIVoxelStudio.UI;

public partial class Main : Node3D
{
    public GameSettings Settings { get; private set; }
    public IBuildProfile Profile { get; private set; }
    public PlayerController Player { get; private set; }
    public Hud Hud { get; private set; }
    public ChatEngine ChatEngine { get; private set; }
    public ChatPanel ChatUi { get; private set; }
    public SettingsDialog SettingsUi { get; private set; }
    public PauseMenu PauseUi { get; private set; }

    private LlmClient _llm;
    private IWorldRenderer _renderer;
    private SubViewport _snapViewport;
    private Camera3D _snapCamera;
    private BlockInventoryOverlay _inventory;
    private RegionSelector _selector;
    private CanvasLayer _selectionHint;

    public override void _Ready()
    {
        Settings = GameSettings.Load();
        if (SaveManager.HasSave(Settings.SaveName)) Settings.BuildKind = SaveManager.ReadKind(Settings.SaveName);

        try
        {
            Profile = BuildProfileFactory.Create(Settings.BuildKind, Settings);
        }
        catch (Exception ex)
        {
            GD.PushError($"无法打开方案: {ex.Message}");
            GetTree().ChangeSceneToFile("res://UI/MainMenu.tscn");
            return;
        }

        SetupEnvironment();
        SetupSnapshot();

        var watch = Stopwatch.StartNew();
        bool loaded = Profile.Serializer.Load(Settings.SaveName);
        if (!loaded && Settings.GenerateGround) Profile.GenerateDefaultWorld();

        _renderer = Profile.CreateRenderer();
        AddChild(_renderer.Node);
        _renderer.BuildAllImmediately();
        bool blank = Profile.RegionCount == 0;

        GD.Print($"World ready: regions={Profile.RegionCount} loaded={loaded} kind={Settings.BuildKind} blank={blank} build={watch.ElapsedMilliseconds}ms");

        SpawnPlayer(blank);
        SetupRegionSelector();
        SetupHud();
        SetupAi();
        SetupPauseMenu();
    }

    /// <summary>进入通用选区模式：保持 FPS 移动/瞄准，隐藏物品栏，两次左键选对角，Y 确认、N/Esc 取消。</summary>
    public void EnterSelectRegionMode(Action<RegionBox> callback, Action cancelled = null)
    {
        Hud.Visible = false;
        if (_selectionHint != null) _selectionHint.Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Captured;

        _selector.Enter(box =>
        {
            Hud.Visible = true;
            if (_selectionHint != null) _selectionHint.Visible = false;
            callback?.Invoke(box);
        }, () =>
        {
            Hud.Visible = true;
            if (_selectionHint != null) _selectionHint.Visible = false;
            cancelled?.Invoke();
        });
    }

    private void SetupRegionSelector()
    {
        _selector = new RegionSelector
        {
            Name = "RegionSelector",
            CameraSource = () => Player.Camera,
            Pick = (origin, direction) => Profile.PickCell(origin, direction, Profile.ReachMeters / Profile.BlockMeters),
            // Q 键：用玩家脚下的方块作为角点
            PlayerCellSource = () => new Vector3I(
                Mathf.FloorToInt(Player.GlobalPosition.X / Profile.BlockMeters),
                Mathf.FloorToInt(Player.GlobalPosition.Y / Profile.BlockMeters),
                Mathf.FloorToInt(Player.GlobalPosition.Z / Profile.BlockMeters)),
            BlockMeters = Profile.BlockMeters,
        };
        AddChild(_selector);

        _selectionHint = GD.Load<PackedScene>("res://UI/SelectionHint.tscn").Instantiate<CanvasLayer>();
        _selectionHint.Name = "SelectionHint";
        _selectionHint.Visible = false;
        AddChild(_selectionHint);

        Profile.AttachRegionSelector(onSelect => EnterSelectRegionMode(onSelect));
    }

    private void SetupEnvironment()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky
            {
                SkyMaterial = new ProceduralSkyMaterial
                {
                    SkyTopColor = new Color(0.29f, 0.51f, 0.84f),
                    SkyHorizonColor = new Color(0.72f, 0.83f, 0.94f),
                    GroundBottomColor = new Color(0.3f, 0.32f, 0.34f),
                },
            },
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.72f, 0.78f, 0.86f),
            AmbientLightEnergy = 0.75f,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
        };
        AddChild(new WorldEnvironment { Environment = env });

        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-52, -38, 0),
            LightEnergy = 1.0f,
            ShadowEnabled = true,
        });
    }

    private void SpawnPlayer(bool blank)
    {
        Player = new PlayerController
        {
            Name = "Player",
            BlockMeters = Profile.BlockMeters,
            ReachMeters = Profile.ReachMeters,
            PlayerWorld = Profile.CreatePlayerWorld(),
            MouseSensitivity = Settings.MouseSensitivity,
            Position = Profile.FindSpawn(blank),
        };
        AddChild(Player);
        if (blank) Player.SetFlying(true);
    }

    private async Task<Image> CaptureSnapshotAsync(Vector3 position, Vector3 lookAt)
    {
        if (_snapViewport == null) return null;

        _snapCamera.Position = position;
        var direction = lookAt - position;
        var up = Math.Abs(direction.Normalized().Dot(Vector3.Up)) > 0.999f ? Vector3.Back : Vector3.Up;
        _snapCamera.LookAt(lookAt, up);
        _snapCamera.Current = true;

        var cell = new Vector3I(
            Mathf.FloorToInt(lookAt.X / Profile.BlockMeters),
            Mathf.FloorToInt(lookAt.Y / Profile.BlockMeters),
            Mathf.FloorToInt(lookAt.Z / Profile.BlockMeters));
        var guides = SnapshotGuides.Create(cell, Profile.BlockMeters);
        AddChild(guides);

        try
        {
            // 渲染在独立线程完成，必须等帧结束才能取到带新相机的画面
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            return _snapViewport.GetTexture()?.GetImage();
        }
        finally
        {
            guides.QueueFree();
        }
    }

    private void SetupSnapshot()
    {
        _snapViewport = new SubViewport
        {
            Size = new Vector2I(640, 360),
            OwnWorld3D = false,
            World3D = GetWorld3D(),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        _snapCamera = new Camera3D { Fov = 70f, Near = 0.05f, Far = 600f };
        _snapViewport.AddChild(_snapCamera);
        AddChild(_snapViewport);
        _snapCamera.Current = true;
    }

    private void SetupHud()
    {
        Hud = GD.Load<PackedScene>("res://UI/Hud.tscn").Instantiate<Hud>();
        Hud.Name = "Hud";
        AddChild(Hud);
        Hud.Bind(Player, Profile.Palette);
        Hud.InventoryRequested += ToggleInventory;
    }

    private void ToggleInventory()
    {
        if (_inventory == null || !IsInstanceValid(_inventory))
        {
            string scenePath = Profile.Palette switch
            {
                McBlockPalette => "res://UI/McInventoryOverlay.tscn",
                VoxelBlockPalette => "res://UI/VoxelInventoryOverlay.tscn",
                _ => null,
            };
            if (scenePath == null) return;

            _inventory = GD.Load<PackedScene>(scenePath).Instantiate<BlockInventoryOverlay>();
            _inventory.SetPalette(Profile.Palette);
            _inventory.Name = "Inventory";
            AddChild(_inventory);
            _inventory.Closed += () => Player.SetInputEnabled(true);
        }

        if (_inventory.IsOpen)
        {
            _inventory.Close();
            return;
        }

        Player.SetInputEnabled(false);
        _inventory.Open();
    }

    private void SetupAi()
    {
        var registry = new ToolRegistry();
        Profile.Tools.Register(registry);

        _llm = new LlmClient { Config = BuildLlmConfig() };

        var store = new ChatStore(Settings.SaveName);
        var context = Profile.CreateToolContext(new ToolContextEnvironment
        {
            PlayerBlock = () => new Vector3I(
                Mathf.FloorToInt(Player.GlobalPosition.X / Profile.BlockMeters),
                Mathf.FloorToInt(Player.GlobalPosition.Y / Profile.BlockMeters),
                Mathf.FloorToInt(Player.GlobalPosition.Z / Profile.BlockMeters)),
            RenderSnapshot = CaptureSnapshotAsync,
            SaveAttachment = image => store.SaveAttachment(image),
            AskUser = questions => ChatUi.AskUserAsync(questions),
        });

        ChatUi = GD.Load<PackedScene>("res://UI/ChatPanel.tscn").Instantiate<ChatPanel>();
        ChatUi.Name = "ChatUi";
        AddChild(ChatUi);

        ChatEngine = new ChatEngine(_llm, registry, context, store, ChatUi)
        {
            PromptPath = Profile.PromptPath,
            MaxToolRounds = Settings.MaxToolRounds,
            MaxSnapshotsPerRequest = Settings.MaxSnapshotsPerRequest,
        };
        ChatEngine.Load();
        ChatUi.Bind(ChatEngine, Player);

        SettingsUi = GD.Load<PackedScene>("res://UI/SettingsDialog.tscn").Instantiate<SettingsDialog>();
        SettingsUi.Name = "SettingsUi";
        AddChild(SettingsUi);
        SettingsUi.Bind(Settings);
        SettingsUi.Saved += () =>
        {
            _llm.Config = BuildLlmConfig();
            ChatEngine.MaxToolRounds = Settings.MaxToolRounds;
            ChatEngine.MaxSnapshotsPerRequest = Settings.MaxSnapshotsPerRequest;
        };
        ChatUi.OpenSettings = () => SettingsUi.Open();
    }

    private LlmConfig BuildLlmConfig() => new()
    {
        BaseUrl = Settings.ApiBaseUrl,
        ApiKey = Settings.ApiKey,
        Model = Settings.Model,
        Temperature = Settings.Temperature,
        ReasoningEffort = Settings.ReasoningEffort,
        ExtraBodyJson = Settings.ExtraBodyJson,
    };

    private void SetupPauseMenu()
    {
        PauseUi = GD.Load<PackedScene>("res://UI/PauseMenu.tscn").Instantiate<PauseMenu>();
        PauseUi.Name = "PauseUi";
        AddChild(PauseUi);
        PauseUi.Bind(Player, ChatUi, () => Settings.SaveName, SaveManager.ListSaves);
        PauseUi.SetExports(BuildExports());
        PauseUi.RegionSelect = (selected, cancelled) => EnterSelectRegionMode(selected, cancelled);
        PauseUi.SaveRequested += SaveGame;
        PauseUi.LoadRequested += name => SwitchSave(name, SaveManager.ReadKind(name), null);
        PauseUi.NewWorldRequested += (name, kind, ground) => SwitchSave(name, kind, ground);
        PauseUi.SettingsRequested += () => SettingsUi.Open();
        PauseUi.MainMenuRequested += ReturnToMenu;
    }

    private void ReturnToMenu()
    {
        SaveGame();
        GetTree().ChangeSceneToFile("res://UI/MainMenu.tscn");
    }

    private IReadOnlyList<ExportEntry> BuildExports()
    {
        var spawn = new Vector3I(
            Mathf.FloorToInt(Player.GlobalPosition.X / Profile.BlockMeters),
            Mathf.FloorToInt(Player.GlobalPosition.Y / Profile.BlockMeters),
            Mathf.FloorToInt(Player.GlobalPosition.Z / Profile.BlockMeters));

        return Profile.Exporters.Select(exporter => new ExportEntry
        {
            Label = exporter.DisplayName,
            Extension = exporter.Extension,
            FilterName = exporter.DisplayName,
            IsDirectory = exporter.IsDirectory,
            RequiresRegion = exporter.RequiresRegion,
            ValidateRegion = exporter.ValidateRegion,
            DefaultDir = exporter.IsDirectory ? McSavesDir() : McStructuresDir(exporter.Id),
            FolderName = SaveManager.Sanitize(Settings.SaveName),
            Run = (path, range, token) =>
            {
                // 目录式导出的 LevelName 跟随用户选择的文件夹名
                string name = exporter.IsDirectory ? Path.GetFileName(path.TrimEnd('/', '\\')) : Settings.SaveName;
                if (string.IsNullOrEmpty(name)) name = Settings.SaveName;
                return exporter.ExportAsync(
                    new ExportRequest { Path = path, SaveName = name, Spawn = spawn, Range = range }, token);
            },
        }).ToList();
    }

    // 结构 NBT 必须放在 .minecraft/structures 才能被结构方块和 /place template 找到；目录不存在时自动创建
    private string McStructuresDir(string exporterId)
    {
        if (exporterId != "mcstructure") return "";
        var root = McAssetsLocator.NormalizeRoot(Settings.McAssetsPath);
        if (string.IsNullOrEmpty(root)) return "";

        string dir = Path.Combine(root, "structures");
        try
        {
            Directory.CreateDirectory(dir);
            return dir;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"无法创建 structures 目录：{ex.Message}");
            return "";
        }
    }

    // 目录式导出优先落在 .minecraft/saves，方便直接开玩
    private string McSavesDir()
    {
        var root = McAssetsLocator.NormalizeRoot(Settings.McAssetsPath);
        if (string.IsNullOrEmpty(root)) return "";
        string saves = Path.Combine(root, "saves");
        return Directory.Exists(saves) ? saves : "";
    }

    private void SwitchSave(string name, BuildKind kind, bool? generateGround)
    {
        if (kind == BuildKind.Minecraft && !EnsureMcAssets()) return;

        SaveGame();
        Settings.SaveName = SaveManager.Sanitize(name);
        Settings.BuildKind = kind;
        if (generateGround.HasValue) Settings.GenerateGround = generateGround.Value;
        Settings.Persist();
        GetTree().ReloadCurrentScene();
    }

    private bool EnsureMcAssets()
    {
        var root = McAssetsLocator.NormalizeRoot(Settings.McAssetsPath);
        if (string.IsNullOrEmpty(root)) root = McAssetsLocator.GuessRoot();
        if (McAssetsLocator.ListVersions(root).Count == 0)
        {
            PauseUi?.SetStatus("未找到可用的我的世界版本，请先在 AI 设置中填写 .minecraft 目录");
            return false;
        }

        Settings.McAssetsPath = root;
        Settings.McVersion = McAssetsLocator.ResolveVersion(root, Settings.McVersion);
        return true;
    }

    public void SaveGame()
    {
        try
        {
            Profile.Serializer.Save(Settings.SaveName);
            PauseUi?.SetStatus("已保存");
            GD.Print("World saved");
        }
        catch (Exception ex)
        {
            GD.PushError($"Save failed: {ex.Message}");
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            SaveGame();
            Settings.Persist();
        }
    }
}
