using Godot;
using AIVoxelStudio.App;
using AIVoxelStudio.Core;
using AIVoxelStudio.Core.Mc;
using AIVoxelStudio.Config;

namespace AIVoxelStudio.UI;

public partial class MainMenu : Control
{
	private GameSettings _settings;
	private ItemList _saves;
	private Button _open;
	private Label _status;
	private SettingsDialog _settingsUi;
	private NewSaveDialog _newDialog;
	private List<string> _saveNames = new();

	private PopupMenu _saveMenu;
	private ConfirmationDialog _renameDialog;
	private LineEdit _renameInput;
	private ConfirmationDialog _confirmDialog;
	private Action _confirmAction;
	private string _contextSave = "";

	public override void _Ready()
	{
		Theme = UiTheme.Create();
		Input.MouseMode = Input.MouseModeEnum.Visible;
		_settings = GameSettings.Load();

		_saves = GetNode<ItemList>("Center/Panel/Margin/Box/SaveList");
		_open = GetNode<Button>("Center/Panel/Margin/Box/OpenButton");
		_status = GetNode<Label>("Center/Panel/Margin/Box/Status");

		_saves.GuiInput += OnSaveListGuiInput;

		_settingsUi = GetNode<SettingsDialog>("SettingsDialog");
		_settingsUi.Bind(_settings);

		_saveMenu = GetNode<PopupMenu>("SaveContextMenu");

		_renameDialog = GetNode<ConfirmationDialog>("RenameSaveDialog");
		_renameInput = _renameDialog.GetNode<LineEdit>("Margin/Box/NameInput");

		_confirmDialog = GetNode<ConfirmationDialog>("ConfirmDialog");
		_newDialog = GetNode<NewSaveDialog>("NewSaveDialog");

		RefreshSaves();
		
		_status.Text = $"版本：{CurrentVersion.VersionString}";
	}

	public void OnSaveSelected(long index) => _open.Disabled = false;

	public void OnSaveActivated(long index) => OpenSelected();

	public void OpenSettings() => _settingsUi.Open();

	public void QuitApp() => GetTree().Quit();

	public void OnConfirmAccepted() => _confirmAction?.Invoke();

	public void OnConfirmCanceled() => _confirmAction = null;

	private void RefreshSaves()
	{
		_saveNames = SaveManager.ListSaves();
		_saves.Clear();
		for (int i = 0; i < _saveNames.Count; i++)
		{
			string name = _saveNames[i];
			string kind = SaveManager.ReadKind(name) == BuildKind.Minecraft ? "我的世界" : "纯色体素";
			int index = _saves.AddItem($"{name}（{kind}）");
			_saves.SetItemMetadata(index, name);
		}
		if (_saveNames.Count == 0) _saves.AddItem("暂无方案");
		_open.Disabled = _saveNames.Count == 0;
	}

	private void OnSaveListGuiInput(InputEvent e)
	{
		if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } mb) return;

		int index = _saves.GetItemAtPosition(mb.Position);
		if (index < 0) return;

		string name = _saves.GetItemMetadata(index).AsString();
		if (string.IsNullOrEmpty(name)) return;

		_saves.Select(index);
		_open.Disabled = false;
		_contextSave = name;
		_saveMenu.Popup(new Rect2I((Vector2I)GetViewport().GetMousePosition(), Vector2I.One));
		_saves.AcceptEvent();
	}

	public void OnSaveMenuPressed(long id)
	{
		switch (id)
		{
			case 0:
				OpenSelected();
				break;
			case 1:
				OpenRenameDialog();
				break;
			case 2:
				RequestDeleteSave();
				break;
		}
	}

	private void OpenRenameDialog()
	{
		if (string.IsNullOrEmpty(_contextSave)) return;
		_renameInput.Text = _contextSave;
		_renameDialog.PopupCentered(new Vector2I(400, 170));
		_renameInput.GrabFocus();
		_renameInput.SelectAll();
	}

	public void RenameSave()
	{
		string oldName = _contextSave;
		if (string.IsNullOrEmpty(oldName)) return;

		string raw = _renameInput.Text.Trim();
		if (raw.Length == 0)
		{
			_status.Text = "请输入新名称";
			return;
		}

		string newName = SaveManager.Sanitize(raw);
		if (newName == oldName) return;
		if (_saveNames.Contains(newName))
		{
			_status.Text = $"方案 {newName} 已存在，请换一个名称";
			return;
		}

		string oldDir = SaveManager.SaveDir(oldName);
		string newDir = SaveManager.SaveDir(newName);
		if (!Directory.Exists(oldDir))
		{
			_status.Text = $"方案 {oldName} 不存在";
			RefreshSaves();
			return;
		}
		if (Directory.Exists(newDir))
		{
			_status.Text = $"目标目录 {newName} 已存在";
			return;
		}

		try
		{
			Directory.Move(oldDir, newDir);
		}
		catch (Exception ex)
		{
			_status.Text = $"重命名失败：{ex.Message}";
			return;
		}

		if (_settings.SaveName == oldName)
		{
			_settings.SaveName = newName;
			_settings.Persist();
		}

		_contextSave = newName;
		RefreshSaves();
		SelectSave(newName);
		_status.Text = $"已重命名为 {newName}";
	}

	private void SelectSave(string name)
	{
		for (int i = 0; i < _saves.ItemCount; i++)
		{
			if (_saves.GetItemMetadata(i).AsString() != name) continue;
			_saves.Select(i);
			_open.Disabled = false;
			return;
		}
	}

	private void RequestDeleteSave()
	{
		string name = _contextSave;
		if (string.IsNullOrEmpty(name)) return;

		string kind = SaveManager.ReadKind(name) == BuildKind.Minecraft ? "我的世界" : "纯色体素";
		_confirmDialog.Title = "删除方案";
		_confirmDialog.DialogText = $"确定删除方案「{name}」（{kind}）？\n存档、对话与截图会一并删除，且不可恢复。";
		_confirmDialog.OkButtonText = "删除";
		_confirmAction = () => DeleteSave(name);
		_confirmDialog.PopupCentered(new Vector2I(440, 180));
	}

	private void DeleteSave(string name)
	{
		_confirmAction = null;
		try
		{
			SaveManager.DeleteSave(name);
		}
		catch (Exception ex)
		{
			_status.Text = $"删除失败：{ex.Message}";
			return;
		}

		if (_settings.SaveName == name)
		{
			_settings.SaveName = "";
			_settings.Persist();
		}

		_contextSave = "";
		RefreshSaves();
		_status.Text = $"已删除方案 {name}";
	}

	public void OpenSelected()
	{
		var selected = _saves.GetSelectedItems();
		if (selected.Length == 0)
		{
			_status.Text = "请选择一个方案";
			return;
		}
		string name = _saves.GetItemMetadata(selected[0]).AsString();
		if (string.IsNullOrEmpty(name)) return;
		Enter(name, null, SaveManager.ReadKind(name));
	}

	private void Enter(string name, bool? generateGround, BuildKind kind)
	{
		if (kind == BuildKind.Minecraft && !EnsureMcAssets()) return;

		_settings.SaveName = SaveManager.Sanitize(name);
		_settings.BuildKind = kind;
		if (generateGround.HasValue) _settings.GenerateGround = generateGround.Value;
		_settings.Persist();
		GetTree().ChangeSceneToFile("res://Main.tscn");
	}

	public void OpenNewDialog()
	{
		_status.Text = "";
		_newDialog.ResetInputs();
		_newDialog.PopupCentered(new Vector2I(400, 240));
		_newDialog.FocusName();
	}

	public void CreateProject()
	{
		_newDialog.Visible = false;
		string raw = _newDialog.SaveNameText.Trim();
		if (raw.Length == 0)
		{
			_status.Text = "请输入方案名称";
			return;
		}

		string name = SaveManager.Sanitize(raw);
		if (_saveNames.Contains(name))
		{
			_status.Text = $"方案 {name} 已存在，请换一个名称";
			return;
		}

		Enter(name, _newDialog.GenerateGround, _newDialog.Kind);
	}

	private bool EnsureMcAssets()
	{
		var root = McAssetsLocator.NormalizeRoot(_settings.McAssetsPath);
		if (string.IsNullOrEmpty(root)) root = McAssetsLocator.GuessRoot();
		var versions = McAssetsLocator.ListVersions(root);
		if (versions.Count == 0)
		{
			_status.Text = "未找到可用的我的世界版本，请先在 AI 设置中填写 .minecraft 目录";
			return false;
		}

		_settings.McAssetsPath = root;
		_settings.McVersion = McAssetsLocator.ResolveVersion(root, _settings.McVersion);
		return true;
	}
}
