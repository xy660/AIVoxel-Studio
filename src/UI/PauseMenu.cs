using Godot;
using AIVoxelStudio.Core;
using AIVoxelStudio.Core.Abstractions;
using AIVoxelStudio.Core.Export;
using AIVoxelStudio.Player;

namespace AIVoxelStudio.UI;

public partial class PauseMenu : CanvasLayer
{
	private PlayerController _player;
	private ChatPanel _chat;
	private Func<string> _currentSave;
	private Func<List<string>> _savesProvider;
	private Control _root;
	private ItemList _saveList;
	private Label _status;
	private NewSaveDialog _newDialog;
	private FileDialog _exportDialog;
	private PopupMenu _exportPopup;
	private ConfirmationDialog _overwriteDialog;
	private string _pendingExportPath = "";
	private IReadOnlyList<ExportEntry> _exports = Array.Empty<ExportEntry>();
	private ExportEntry _pendingExport;
	private RegionBox? _pendingRange;
	private Action _confirmAction;
	private List<string> _saves = new();

	/// <summary>由组合根注入：进入选区模式，确认回调 / 取消回调。</summary>
	public Action<Action<RegionBox>, Action> RegionSelect { get; set; }

	public bool IsOpen { get; private set; }

	public event Action SaveRequested;
	public event Action<string> LoadRequested;
	public event Action<string, BuildKind, bool> NewWorldRequested;
	public event Action SettingsRequested;
	public event Action MainMenuRequested;

	public override void _Ready()
	{
		_root = GetNode<Control>("PauseRoot");
		_root.Theme = UiTheme.Create();
		_saveList = GetNode<ItemList>("PauseRoot/Center/Panel/Box/SaveList");
		_status = GetNode<Label>("PauseRoot/Center/Panel/Box/Status");
		_newDialog = GetNode<NewSaveDialog>("NewSaveDialog");

		_overwriteDialog = GD.Load<PackedScene>("res://UI/ConfirmDialog.tscn").Instantiate<ConfirmationDialog>();
		AddChild(_overwriteDialog);
		_overwriteDialog.Confirmed += () =>
		{
			var action = _confirmAction;
			_confirmAction = null;
			action?.Invoke();
		};
		_overwriteDialog.Canceled += () =>
		{
			_confirmAction = null;
			_pendingExportPath = "";
		};
	}

	public void RequestSave() => SaveRequested?.Invoke();

	public void OpenSettings() => SettingsRequested?.Invoke();

	public void ReturnToMainMenu()
	{
		Close();
		MainMenuRequested?.Invoke();
	}

	public void Bind(PlayerController player, ChatPanel chat, Func<string> currentSave, Func<List<string>> saves)
	{
		_player = player;
		_chat = chat;
		_currentSave = currentSave;
		_savesProvider = saves;
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (e is not InputEventKey { Pressed: true, Echo: false } key || key.Keycode != Key.Escape) return;
		if (_chat is { IsOpen: true }) return;
		// 背包等其它模态占用输入时不抢 Esc
		if (!IsOpen && _player is { InputEnabled: false }) return;
		if (_newDialog is { Visible: true } || _exportDialog is { Visible: true } ||
			_overwriteDialog is { Visible: true }) return;

		Toggle();
		GetViewport().SetInputAsHandled();
	}

	public void Open()
	{
		if (IsOpen) return;
		IsOpen = true;
		_root.Visible = true;
		_saveList.Visible = false;
		_status.Text = "";
		_player?.SetInputEnabled(false);
		RefreshSaves();
	}

	public void Close()
	{
		if (!IsOpen) return;
		IsOpen = false;
		_root.Visible = false;
		_saveList.Visible = false;
		_player?.SetInputEnabled(true);
	}

	public void Toggle()
	{
		if (IsOpen) Close();
		else Open();
	}

	public void SetStatus(string text)
	{
		if (_status != null) _status.Text = text;
	}

	private void RefreshSaves()
	{
		if (_savesProvider == null) return;
		_saves = _savesProvider();
		_saveList.Clear();

		string current = _currentSave?.Invoke() ?? "";
		for (int i = 0; i < _saves.Count; i++)
		{
			string name = _saves[i];
			string kind = SaveManager.ReadKind(name) == BuildKind.Minecraft ? "我的世界" : "纯色体素";
			string label = name == current ? $"{name}（{kind} · 当前）" : $"{name}（{kind}）";
			int index = _saveList.AddItem(label);
			_saveList.SetItemMetadata(index, name);
			if (name == current) _saveList.Select(index);
		}

		if (_saves.Count == 0) _saveList.AddItem("暂无方案");
	}

	public void ToggleSaves()
	{
		_saveList.Visible = !_saveList.Visible;
		if (_saveList.Visible) RefreshSaves();
	}

	public void LoadSelected(long index)
	{
		string name = _saveList.GetItemMetadata((int)index).AsString();
		if (string.IsNullOrEmpty(name)) return;
		LoadRequested?.Invoke(name);
	}

	public void SetExports(IReadOnlyList<ExportEntry> exports)
	{
		_exports = exports;
		var button = GetNode<Button>("PauseRoot/Center/Panel/Box/ExportButton");
		button.Text = exports.Count switch
		{
			0 => "导出（无可用格式）",
			1 => exports[0].Label,
			_ => "导出…",
		};
		button.Disabled = exports.Count == 0;
	}

	public void OnExportPressed()
	{
		if (_exports.Count == 0) return;
		if (_exports.Count == 1)
		{
			StartExport(_exports[0]);
			return;
		}

		_exportPopup ??= CreateExportPopup();
		_exportPopup.Clear();
		for (int i = 0; i < _exports.Count; i++) _exportPopup.AddItem(_exports[i].Label, i);

		var button = GetNode<Button>("PauseRoot/Center/Panel/Box/ExportButton");
		// 用按钮在视口里的全局矩形定位，避免 PopupOnParent 把局部坐标当全局坐标
		var rect = button.GetGlobalRect();
		_exportPopup.PopupOnParent(new Rect2I((Vector2I)rect.Position, (Vector2I)rect.Size));
	}

	private PopupMenu CreateExportPopup()
	{
		var button = GetNode<Button>("PauseRoot/Center/Panel/Box/ExportButton");
		var popup = new PopupMenu { Name = "ExportPopup" };
		button.GetParent().AddChild(popup);
		popup.IdPressed += id =>
		{
			if (id >= 0 && id < _exports.Count) StartExport(_exports[(int)id]);
		};
		return popup;
	}

	// 需要选区的导出：先关菜单进选区，拿到包围盒（必要时先弹警示）再弹保存对话框
	private void StartExport(ExportEntry entry)
	{
		if (!entry.RequiresRegion || RegionSelect == null)
		{
			OpenExportDialog(entry, null);
			return;
		}

		Close();
		RegionSelect(box =>
		{
			Open();

			string warning = entry.ValidateRegion?.Invoke(box);
			if (!string.IsNullOrEmpty(warning))
			{
				_pendingExport = entry;
				_pendingRange = box;
				_confirmAction = () => OpenExportDialog(entry, box);
				_overwriteDialog.Title = "选区超出限制";
				_overwriteDialog.DialogText = warning;
				_overwriteDialog.OkButtonText = "继续导出";
				_overwriteDialog.PopupCentered(new Vector2I(560, 220));
				return;
			}

			OpenExportDialog(entry, box);
		}, Open);
	}

	private void OpenExportDialog(ExportEntry entry, RegionBox? range)
	{
		EnsureExportDialog();

		_pendingExport = entry;
		_pendingRange = range;
		_exportDialog.Title = entry.Label;

		if (entry.IsDirectory)
		{
			_exportDialog.FileMode = FileDialog.FileModeEnum.SaveFile;
			_exportDialog.ClearFilters();
			_exportDialog.CurrentDir = entry.DefaultDir;
			_exportDialog.CurrentFile = string.IsNullOrEmpty(entry.FolderName) ? "world" : entry.FolderName;
			_exportDialog.PopupCentered(new Vector2I(900, 600));
			return;
		}

		_exportDialog.FileMode = FileDialog.FileModeEnum.SaveFile;
		_exportDialog.CurrentDir = string.IsNullOrEmpty(entry.DefaultDir)
			? ProjectSettings.GlobalizePath("user://exports")
			: entry.DefaultDir;
		_exportDialog.ClearFilters();
		_exportDialog.AddFilter($"*.{entry.Extension}",
			string.IsNullOrEmpty(entry.FilterName) ? entry.Extension.ToUpperInvariant() : entry.FilterName);

		string save = SaveManager.Sanitize(_currentSave?.Invoke() ?? "world");
		_exportDialog.CurrentFile = $"{save}-{DateTime.Now:yyyyMMdd-HHmmss}.{entry.Extension}";
		_exportDialog.PopupCentered(new Vector2I(900, 600));
	}

	private void EnsureExportDialog()
	{
		if (_exportDialog != null) return;

		_exportDialog = new FileDialog
		{
			Access = FileDialog.AccessEnum.Filesystem,
			UseNativeDialog = true,
		};
		_exportDialog.FileSelected += OnExportPathChosen;
		AddChild(_exportDialog);
	}

	private void OnExportPathChosen(string path)
	{
		var entry = _pendingExport;
		if (entry == null) return;

		if (!entry.IsDirectory)
		{
			RunExport(path);
			return;
		}

		string target = path;
		if (!Directory.Exists(target))
		{
			RunExport(target);
			return;
		}

		_pendingExportPath = target;
		_confirmAction = ConfirmOverwriteExport;
		_overwriteDialog.Title = "覆盖世界";
		_overwriteDialog.DialogText = $"目标位置已存在世界文件夹，覆盖后旧存档文件会被删除：\n{target}";
		_overwriteDialog.OkButtonText = "覆盖";
		_overwriteDialog.PopupCentered(new Vector2I(540, 200));
	}

	private void ConfirmOverwriteExport()
	{
		string path = _pendingExportPath;
		_pendingExportPath = "";
		if (!string.IsNullOrEmpty(path)) RunExport(path);
	}

	private async void RunExport(string path)
	{
		var entry = _pendingExport;
		if (entry == null) return;

		var watch = System.Diagnostics.Stopwatch.StartNew();
		try
		{
			var result = await entry.Run(path, _pendingRange, CancellationToken.None);
			SetStatus(result.Ok
				? $"{result.Message}（{watch.ElapsedMilliseconds}ms）\n{path}"
				: result.Message);
		}
		catch (Exception ex)
		{
			SetStatus($"导出失败：{ex.Message}");
			GD.PushError($"Export failed ({entry.Extension}): {ex.Message}");
		}
	}

	public void OpenNewDialog()
	{
		_newDialog.ResetInputs();
		_saveList.Visible = false;
		_newDialog.PopupCentered(new Vector2I(400, 240));
		_newDialog.FocusName();
	}

	public void CreateWorld()
	{
		string raw = _newDialog.SaveNameText.Trim();
		if (raw.Length == 0)
		{
			SetStatus("请输入方案名称");
			return;
		}

		string name = SaveManager.Sanitize(raw);
		if (_saves.Contains(name))
		{
			SetStatus($"方案 {name} 已存在，请换一个名称");
			return;
		}

		NewWorldRequested?.Invoke(name, _newDialog.Kind, _newDialog.GenerateGround);
	}
}
