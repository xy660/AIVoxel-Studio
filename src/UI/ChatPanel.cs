using System.Text;
using Godot;
using AIVoxelStudio.AI;
using AIVoxelStudio.Player;

namespace AIVoxelStudio.UI;

public partial class ChatPanel : CanvasLayer, IUiCallback
{
	private ChatEngine _engine;
	private PlayerController _player;
	private PanelContainer _panel;
	private VBoxContainer _messageBox;
	private ScrollContainer _scroll;
	private LineEdit _input;
	private Button _sendButton;
	private Label _status;
	private bool _busy;

	private Button _historyButton;
	private HistoryMenu _historyMenu;
	private PopupMenu _sessionMenu;
	private ConfirmationDialog _confirmDialog;
	private List<ChatSessionInfo> _historySessions = new();
	private string _pendingDeleteId = "";
	private HBoxContainer _pendingBox;
	private FileDialog _imageDialog;
	private readonly List<string> _pendingImages = new();
	private const int MaxImagesPerMessage = 4;

	private static readonly Color UserBubbleColor = new(0.16f, 0.29f, 0.47f, 0.92f);
	private static readonly Color AiBubbleColor = new(0.155f, 0.175f, 0.215f, 0.96f);

	private VBoxContainer _streamBox;
	private Label _streamLabel;
	private Label _reasoningLabel;
	private Button _reasoningToggle;
	private bool _reasoningExpanded;
	private readonly StringBuilder _pendingContent = new();
	private readonly StringBuilder _pendingReasoning = new();

	public Action OpenSettings { get; set; }
	public bool IsOpen { get; private set; }

	public override void _Ready()
	{
		GetNode<Control>("ChatRoot").Theme = UiTheme.Create();

		_panel = GetNode<PanelContainer>("ChatRoot/ChatPanel");
		_scroll = GetNode<ScrollContainer>("ChatRoot/ChatPanel/Main/Scroll");
		_messageBox = GetNode<VBoxContainer>("ChatRoot/ChatPanel/Main/Scroll/Messages");
		_input = GetNode<LineEdit>("ChatRoot/ChatPanel/Main/InputRow/Input");
		_sendButton = GetNode<Button>("ChatRoot/ChatPanel/Main/InputRow/SendButton");
		_status = GetNode<Label>("ChatRoot/ChatPanel/Main/Status");
		_pendingBox = GetNode<HBoxContainer>("ChatRoot/ChatPanel/Main/PendingImages");
		_imageDialog = GetNode<FileDialog>("ChatRoot/ImageDialog");

		_imageDialog.FileSelected += OnImageFileSelected;

		_input.TextSubmitted += _ =>
		{
			if (!_busy) SendCurrent();
		};
		_historyButton = GetNode<Button>("ChatRoot/ChatPanel/Main/Header/HistoryButton");

		_historyMenu = GetNode<HistoryMenu>("ChatRoot/ChatPanel/Main/HistoryMenu");
		_historyMenu.SessionChosen += OnHistorySelected;
		_historyMenu.SessionDeleteRequested += OnHistoryRightClicked;

		_sessionMenu = GetNode<PopupMenu>("ChatRoot/ChatPanel/Main/SessionContextMenu");
		_sessionMenu.IdPressed += _ => RequestDeleteSession(_pendingDeleteId);

		_confirmDialog = GetNode<ConfirmationDialog>("ConfirmDialog");
	}

	public void OpenImageDialog() => _imageDialog.PopupCentered();

	public void OpenSettingsPanel() => OpenSettings?.Invoke();

	public void OnConfirmCanceled() => _pendingDeleteId = "";

	public void Bind(ChatEngine engine, PlayerController player)
	{
		_engine = engine;
		_player = player;
		RefreshHistory();
	}

	public override void _Input(InputEvent e)
	{
		if (!IsOpen || e is not InputEventKey { Pressed: true, Echo: false } key) return;
		if (key.Keycode != Key.V || !key.CtrlPressed || !DisplayServer.ClipboardHasImage()) return;

		var image = DisplayServer.ClipboardGetImage();
		if (image != null) AttachImage(image);
		GetViewport().SetInputAsHandled();
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (e is not InputEventKey { Pressed: true, Echo: false } key) return;

		if (key.Keycode == Key.T && !IsOpen && (_player == null || _player.InputEnabled))
		{
			Open();
			GetViewport().SetInputAsHandled();
		}
		else if (key.Keycode == Key.Escape && IsOpen)
		{
			Close();
			GetViewport().SetInputAsHandled();
		}
	}

	public void Open()
	{
		if (IsOpen) return;
		IsOpen = true;
		_panel.Visible = true;
		_player?.SetInputEnabled(false);
		_input.GrabFocus();
	}

	public void Close()
	{
		if (!IsOpen) return;
		IsOpen = false;
		_panel.Visible = false;
		_input.ReleaseFocus();
		_player?.SetInputEnabled(true);
	}

	public void Toggle()
	{
		if (IsOpen) Close();
		else Open();
	}

	public void NewSession()
	{
		if (_busy)
		{
			AddSystemLine("AI 正在生成，请先停止再新建对话");
			return;
		}
		_engine?.NewSession();
		RefreshHistory();
		AddSystemLine("已开始新对话");
	}

	public void ShowHistoryMenu()
	{
		if (_engine == null) return;
		if (_busy)
		{
			AddSystemLine("AI 正在生成，请先停止再切换对话");
			return;
		}

		var sessions = _engine.Sessions.OrderByDescending(s => s.UpdatedAt).ToList();
		_historySessions = sessions;
		_historyMenu.ShowSessions(sessions, _engine.Session?.Id);
		_historyMenu.Size = _historyMenu.ContentSize();
		_historyMenu.PopupOnParent(new Rect2I(
			new Vector2I((int)_historyButton.GetScreenPosition().X,
				(int)(_historyButton.GetScreenPosition().Y + _historyButton.Size.Y)),
			Vector2I.One));
	}

	private void OnHistorySelected(int index)
	{
		if (_engine == null) return;
		if (index < 0 || index >= _historySessions.Count) return;

		string id = _historySessions[index].Id;
		if (id == _engine.Session?.Id) return;

		_engine.SwitchSession(id);
		RefreshHistory();
		AddSystemLine($"已切换到对话：{_engine.Session?.Title}");
	}

	private void OnHistoryRightClicked(int index)
	{
		if (_engine == null) return;
		if (_busy)
		{
			AddSystemLine("AI 正在生成，请先停止再删除对话");
			return;
		}
		if (index < 0 || index >= _historySessions.Count) return;

		_pendingDeleteId = _historySessions[index].Id;
		CallDeferred(nameof(OpenSessionMenu));
	}

	// 延后一帧弹出，避免同一次右键事件把刚弹出的菜单立刻关掉
	private void OpenSessionMenu()
	{
		_sessionMenu.Popup(new Rect2I((Vector2I)GetViewport().GetMousePosition(), Vector2I.One));
	}

	private void RequestDeleteSession(string id)
	{
		if (_engine == null || string.IsNullOrEmpty(id)) return;

		string title = _historySessions.FirstOrDefault(s => s.Id == id)?.Title ?? "新对话";
		if (string.IsNullOrWhiteSpace(title)) title = "新对话";

		_pendingDeleteId = id;
		_confirmDialog.Title = "删除对话";
		_confirmDialog.DialogText = $"确定删除对话「{title}」？删除后不可恢复。";
		_confirmDialog.OkButtonText = "删除";
		_confirmDialog.PopupCentered(new Vector2I(420, 170));
	}

	public void DeletePendingSession()
	{
		string id = _pendingDeleteId;
		_pendingDeleteId = "";
		if (string.IsNullOrEmpty(id) || _engine == null) return;

		if (!_engine.DeleteSession(id))
		{
			AddSystemLine("AI 正在生成，无法删除对话");
			return;
		}

		RefreshHistory();
		_historyMenu.Hide();
		AddSystemLine("已删除对话");
	}

	public void OnSendPressed()
	{
		if (_busy)
		{
			_engine?.Cancel();
			return;
		}
		SendCurrent();
	}

	private async void SendCurrent()
	{
		if (_engine == null || _engine.Busy) return;

		string text = _input.Text.Trim();
		var images = new List<string>(_pendingImages);
		if (text.Length == 0 && images.Count == 0) return;

		_input.Text = "";
		_pendingImages.Clear();
		RefreshPendingImages();
		AddBubbleMessage(true, text, images);
		_input.GrabFocus();
		await _engine.SendAsync(text, images);
	}

	private void OnImageFileSelected(string path)
	{
		var image = Image.LoadFromFile(path);
		if (image == null)
		{
			AddSystemLine($"无法读取图片：{path}");
			return;
		}
		AttachImage(image);
	}

	private void AttachImage(Image image)
	{
		if (_pendingImages.Count >= MaxImagesPerMessage)
		{
			AddSystemLine($"一条消息最多 {MaxImagesPerMessage} 张图片");
			return;
		}

		string name = _engine?.SaveAttachment(NormalizeImage(image));
		if (string.IsNullOrEmpty(name)) return;
		_pendingImages.Add(name);
		RefreshPendingImages();
	}

	private static Image NormalizeImage(Image image)
	{
		const int maxWidth = 1280;
		const int maxHeight = 720;
		int width = image.GetWidth();
		int height = image.GetHeight();
		if (width <= maxWidth && height <= maxHeight) return image;

		float scale = Math.Min(maxWidth / (float)width, maxHeight / (float)height);
		image.Resize(Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)), Image.Interpolation.Bilinear);
		return image;
	}

	private void RefreshPendingImages()
	{
		foreach (var child in _pendingBox.GetChildren())
		{
			_pendingBox.RemoveChild(child);
			child.QueueFree();
		}

		foreach (var name in _pendingImages)
		{
			string imageName = name;
			_pendingBox.AddChild(CreateThumbChip(imageName, ThumbnailSize, () =>
			{
				_pendingImages.Remove(imageName);
				RefreshPendingImages();
			}));
		}
		_pendingBox.Visible = _pendingImages.Count > 0;
	}

	private const float ThumbnailSize = 56f;
	private const float BubbleImageSize = 160f;

	private Control CreateThumbChip(string name, float maxSize, Action remove)
	{
		var panel = new PanelContainer();
		var style = new StyleBoxFlat
		{
			BgColor = new Color(0f, 0f, 0f, 0.35f),
			ContentMarginLeft = 6,
			ContentMarginRight = 4,
			ContentMarginTop = 4,
			ContentMarginBottom = 4,
		};
		style.SetCornerRadiusAll(6);
		panel.AddThemeStyleboxOverride("panel", style);

		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 4);
		panel.AddChild(row);

		var texture = LoadAttachmentTexture(name);
		if (texture != null) row.AddChild(CreateTextureRect(texture, maxSize));

		var close = new Button
		{
			Text = "×",
			Flat = true,
			FocusMode = Control.FocusModeEnum.None,
			CustomMinimumSize = new Vector2(22, 0),
		};
		close.Pressed += remove;
		row.AddChild(close);
		return panel;
	}

	private void AddImageRow(VBoxContainer box, IReadOnlyList<string> images)
	{
		if (images is not { Count: > 0 }) return;

		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);
		foreach (var name in images)
		{
			var texture = LoadAttachmentTexture(name);
			if (texture != null) row.AddChild(CreateTextureRect(texture, BubbleImageSize));
		}
		if (row.GetChildCount() > 0) box.AddChild(row);
	}

	private static TextureRect CreateTextureRect(Texture2D texture, float maxSize)
	{
		float width = texture.GetWidth();
		float height = texture.GetHeight();
		float scale = Math.Min(1f, maxSize / Math.Max(width, height));
		return new TextureRect
		{
			Texture = texture,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			CustomMinimumSize = new Vector2(width * scale, height * scale),
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
		};
	}

	private Texture2D LoadAttachmentTexture(string name)
	{
		var image = _engine?.LoadAttachment(name);
		return image == null ? null : ImageTexture.CreateFromImage(image);
	}

	private void RefreshHistory()
	{
		foreach (var child in _messageBox.GetChildren())
		{
			_messageBox.RemoveChild(child);
			child.QueueFree();
		}
		_streamBox = null;
		_streamLabel = null;
		_reasoningLabel = null;
		_reasoningToggle = null;
		_reasoningExpanded = false;
		_pendingContent.Clear();
		_pendingReasoning.Clear();
		if (_engine?.Session == null) return;

		foreach (var message in _engine.Session.Messages)
		{
			if (message.Role == "user")
			{
				if (message.Content.StartsWith("（系统：本轮 RenderSnapshot"))
					AddBubbleMessage(false, "AI 预览图", message.Images);
				else
					AddBubbleMessage(true, message.Content, message.Images);
			}
			else if (message.Role == "assistant" && !string.IsNullOrWhiteSpace(message.Content))
				AddBubbleMessage(false, message.Content);
		}
		ScrollToBottom();
	}

	private void AddBubbleMessage(bool user, string text, IReadOnlyList<string> images = null)
	{
		var box = CreateBubble(user, out _);
		AddImageRow(box, images);

		if (!string.IsNullOrEmpty(text))
		{
			var label = new Label
			{
				Text = text,
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
				CustomMinimumSize = new Vector2(MeasureBubbleWidth(text), 0),
			};
			if (user) label.AddThemeColorOverride("font_color", new Color(0.94f, 0.96f, 1f));
			box.AddChild(label);
		}
		ScrollToBottom();
	}

	private VBoxContainer CreateBubble(bool user, out PanelContainer panel)
	{
		var row = new HBoxContainer();
		if (user) row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

		panel = new PanelContainer();
		var style = new StyleBoxFlat
		{
			BgColor = user ? UserBubbleColor : AiBubbleColor,
			BorderColor = user ? new Color(0.4f, 0.6f, 0.95f, 0.4f) : new Color(1, 1, 1, 0.12f),
			ContentMarginLeft = 10,
			ContentMarginRight = 10,
			ContentMarginTop = 8,
			ContentMarginBottom = 8,
		};
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(8);
		panel.AddThemeStyleboxOverride("panel", style);

		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 4);
		panel.AddChild(box);

		row.AddChild(panel);
		if (!user) row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
		_messageBox.AddChild(row);
		return box;
	}

	private float MeasureBubbleWidth(string text)
	{
		var font = _panel.GetThemeFont("font");
		int fontSize = _panel.GetThemeFontSize("font_size");
		float width = font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize).X + 28f;
		return Mathf.Clamp(width, 64f, Mathf.Max(160f, _panel.Size.X * 0.84f));
	}

	private void AddSystemLine(string text)
	{
		var label = new Label
		{
			Text = text,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		label.AddThemeFontSizeOverride("font_size", 12);
		label.AddThemeColorOverride("font_color", new Color(0.7f, 0.75f, 0.72f));
		_messageBox.AddChild(label);
		ScrollToBottom();
	}

	private void ScrollToBottom()
	{
		Callable.From(ScrollToBottomNow).CallDeferred();
	}

	private void ScrollToBottomNow()
	{
		if (!IsInstanceValid(_scroll)) return;
		_scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue;
		Callable.From(() =>
		{
			if (IsInstanceValid(_scroll)) _scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue;
		}).CallDeferred();
	}

	public override void _Process(double delta)
	{
		if (_pendingReasoning.Length > 0) FlushReasoning();
		if (_pendingContent.Length > 0) FlushContent();
	}

	public void OnSnapshot(string attachment)
	{
		Callable.From(() => AddBubbleMessage(false, "AI 预览图", new[] { attachment })).CallDeferred();
	}

	public void OnDelta(string text) =>
		Callable.From(() =>
		{
			if (!IsInstanceValid(this)) return;
			_pendingContent.Append(text);
		}).CallDeferred();

	public void OnReasoningDelta(string text) =>
		Callable.From(() =>
		{
			if (!IsInstanceValid(this)) return;
			_pendingReasoning.Append(text);
		}).CallDeferred();

	public void OnAssistantMessage(string text) =>
		Callable.From(() => FinalizeAssistant(text)).CallDeferred();

	private void FinalizeAssistant(string text)
	{
		if (!IsInstanceValid(this)) return;
		if (_pendingReasoning.Length > 0) FlushReasoning();
		if (_pendingContent.Length > 0) FlushContent();

		SetReasoningExpanded(false);

		if (!string.IsNullOrEmpty(text))
		{
			if (_streamLabel != null && IsInstanceValid(_streamLabel))
			{
				_streamLabel.Text = text;
				_streamLabel.CustomMinimumSize = new Vector2(MeasureBubbleWidth(text), 0);
			}
			else
			{
				AddBubbleMessage(false, text);
			}
		}

		_streamBox = null;
		_streamLabel = null;
		_reasoningLabel = null;
		_reasoningToggle = null;
		_reasoningExpanded = false;
		ScrollToBottom();
	}

	private VBoxContainer EnsureStreamBubble()
	{
		if (_streamBox != null && IsInstanceValid(_streamBox)) return _streamBox;
		_streamBox = CreateBubble(false, out _);
		return _streamBox;
	}

	private void FlushContent()
	{
		var box = EnsureStreamBubble();
		if (_streamLabel == null || !IsInstanceValid(_streamLabel))
		{
			_streamLabel = new Label
			{
				Text = "",
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
				CustomMinimumSize = new Vector2(StreamWidth(), 0),
			};
			_streamLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.95f, 0.9f));
			box.AddChild(_streamLabel);
		}

		if (_pendingContent.Length == 0) return;
		SetReasoningExpanded(false);
		_streamLabel.Text += _pendingContent.ToString();
		_pendingContent.Clear();
		ScrollToBottom();
	}

	private void FlushReasoning()
	{
		var box = EnsureStreamBubble();
		if (_reasoningLabel == null || !IsInstanceValid(_reasoningLabel))
		{
			var body = new Label
			{
				Text = "",
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
				CustomMinimumSize = new Vector2(StreamWidth(), 0),
				Modulate = new Color(0.72f, 0.8f, 0.74f, 0.9f),
			};
			body.AddThemeFontSizeOverride("font_size", 12);

			var toggle = new Button
			{
				Text = "思考过程 ▾",
				Flat = true,
				Alignment = HorizontalAlignment.Left,
				FocusMode = Control.FocusModeEnum.None,
			};
			toggle.AddThemeFontSizeOverride("font_size", 12);
			toggle.Modulate = new Color(1, 1, 1, 0.6f);
			toggle.Pressed += () =>
			{
				body.Visible = !body.Visible;
				toggle.Text = body.Visible ? "思考过程 ▾" : "思考过程 ▸";
			};

			box.AddChild(toggle);
			box.AddChild(body);
			_reasoningToggle = toggle;
			_reasoningLabel = body;
			_reasoningExpanded = true;
		}

		if (_pendingReasoning.Length == 0) return;
		_reasoningLabel.Text += _pendingReasoning.ToString();
		_pendingReasoning.Clear();
		ScrollToBottom();
	}

	private void SetReasoningExpanded(bool expanded)
	{
		if (_reasoningLabel == null || !IsInstanceValid(_reasoningLabel)) return;
		_reasoningExpanded = expanded;
		_reasoningLabel.Visible = expanded;
		_reasoningToggle.Text = expanded ? "思考过程 ▾" : "思考过程 ▸";
	}

	private float StreamWidth() => Mathf.Max(180f, _panel.Size.X * 0.84f - 22f);

	public void OnToolStarted(string name, string arguments) =>
		Callable.From(() =>
		{
			if (!IsInstanceValid(this)) return;
			AddSystemLine($"调用工具 {name}");
		}).CallDeferred();

	public void OnToolFinished(string name, bool ok, string content) =>
		Callable.From(() =>
		{
			if (!IsInstanceValid(this)) return;
			string brief = content.Length > 120 ? content[..120] + "…" : content;
			AddSystemLine(ok ? $"{name} 完成：{brief}" : $"{name} 失败：{brief}");
		}).CallDeferred();

	public void OnError(string message) =>
		Callable.From(() =>
		{
			if (!IsInstanceValid(this)) return;
			AddSystemLine($"错误：{message}");
			_status.Text = "";
		}).CallDeferred();

	public void OnStopped() =>
		Callable.From(() =>
		{
			if (!IsInstanceValid(this)) return;
			AddSystemLine("已停止生成");
		}).CallDeferred();

	public void OnBusyChanged(bool busy) =>
		Callable.From(() =>
		{
			if (!IsInstanceValid(this)) return;
			_busy = busy;
			if (busy)
			{
				_streamBox = null;
				_streamLabel = null;
				_reasoningLabel = null;
				_reasoningToggle = null;
				_reasoningExpanded = false;
			}
			_sendButton.Text = busy ? "停止" : "发送";
			_status.Text = busy ? "AI 正在生成…" : "";
		}).CallDeferred();
}
