using Godot;

namespace AIVoxelStudio.AI;

public sealed class ChatEngine
{
	private const int MaxToolResultChars = 4000;

	/// <summary>单次请求最多允许的模型轮次（含工具调用），从设置读取。</summary>
	public int MaxToolRounds { get; set; } = 30;

	/// <summary>当前模式的静态提示词路径，由组合根注入。</summary>
	public string PromptPath { get; set; } = "res://Prompts/voxel/system_prompt.md";

	/// <summary>单次请求最多允许的截图次数，从设置读取。</summary>
	public int MaxSnapshotsPerRequest { get; set; } = 5;

	private readonly LlmClient _llm;
	private readonly ToolRegistry _registry;
	private readonly ToolContext _toolContext;
	private readonly ChatStore _store;
	private readonly IUiCallback _ui;
	private CancellationTokenSource _cts;

	public ChatSession Session { get; private set; }
	public bool Busy { get; private set; }
	public IReadOnlyList<ChatSessionInfo> Sessions => _store.Sessions;

	public ChatEngine(
		LlmClient llm,
		ToolRegistry registry,
		ToolContext toolContext,
		ChatStore store,
		IUiCallback ui)
	{
		_llm = llm;
		_registry = registry;
		_toolContext = toolContext;
		_store = store;
		_ui = ui;
	}

	public void Load()
	{
		_store.LoadIndex();
		Session = _store.LoadActiveOrLatest();
	}

	public void NewSession()
	{
		Session = _store.CreateSession();
	}

	public void SwitchSession(string id)
	{
		if (string.IsNullOrWhiteSpace(id) || id == Session?.Id) return;
		var session = _store.Load(id);
		if (session == null) return;
		_store.SetActive(id);
		Session = session;
	}

	/// <summary>删除一个对话；若删的是当前对话则切到最新会话，没有则新建。</summary>
	public bool DeleteSession(string id)
	{
		if (Busy || string.IsNullOrWhiteSpace(id)) return false;

		bool current = Session?.Id == id;
		_store.Delete(id);
		if (!current) return true;

		Session = null;
		var latest = _store.Sessions.OrderByDescending(s => s.UpdatedAt).FirstOrDefault();
		if (latest != null)
		{
			Session = _store.Load(latest.Id);
			if (Session != null) _store.SetActive(latest.Id);
		}
		if (Session == null) Session = _store.CreateSession();
		return true;
	}

	public async Task SendAsync(string text, List<string> images = null)
	{
		if (Busy || (string.IsNullOrWhiteSpace(text) && images is not { Count: > 0 })) return;

		if (Session == null) Load();

		Busy = true;
		_cts?.Cancel();
		_cts = new CancellationTokenSource();
		var token = _cts.Token;
		_ui.OnBusyChanged(true);

		var userMessage = ChatMessage.User(text?.Trim() ?? "", images);
		Session.Messages.Add(userMessage);
		if (Session.Title == "新对话")
		{
			string title = userMessage.Content;
			if (string.IsNullOrWhiteSpace(title)) title = $"图片 ×{images.Count}";
			Session.Title = title.Length > 20 ? title[..20] + "…" : title;
		}
		_store.Save(Session);

		int maxRounds = Math.Max(1, MaxToolRounds);
		int maxSnapshots = Math.Max(1, MaxSnapshotsPerRequest);
		int snapshotsUsed = 0;

		try
		{
			for (int round = 0; round < maxRounds; round++)
			{
				var messages = new List<ChatMessage> { ChatMessage.System(BuildSystemPrompt()) };
				messages.AddRange(SanitizeOrder(Session.Messages));

				var reply = await _llm.CompleteStreamAsync(messages, _registry.BuildSchemas(),
					_ui.OnDelta, _ui.OnReasoningDelta, _store.AttachmentDataUrl, token);
				if (token.IsCancellationRequested) reply.ToolCalls = null;
				Session.Messages.Add(reply);

				_ui.OnAssistantMessage(reply.Content);

				if (token.IsCancellationRequested)
				{
					_ui.OnStopped();
					return;
				}

				if (reply.ToolCalls is not { Count: > 0 })
				{
					_store.Save(Session);
					return;
				}

				var snapshotImages = new List<string>();

				foreach (var call in reply.ToolCalls)
				{
					if (token.IsCancellationRequested) break;

					if (call.Name == "RenderSnapshot" && snapshotsUsed >= maxSnapshots)
					{
						var denied = ToolResult.Failure(
							$"本轮截图次数已用完（上限 {maxSnapshots} 次），不要再调用 RenderSnapshot，请根据已有预览图和文字结果继续或总结。");
						_ui.OnToolFinished(call.Name, false, denied.Content);
						Session.Messages.Add(ChatMessage.ToolResult(call.Id, call.Name, denied.Content));
						continue;
					}

					_ui.OnToolStarted(call.Name, call.Arguments);
					var result = await _registry.InvokeAsync(call.Name, call.Arguments, _toolContext);
					_ui.OnToolFinished(call.Name, result.Ok, result.Content);

					string content = result.Content;
					if (content.Length > MaxToolResultChars) content = content[..MaxToolResultChars] + "…";
					Session.Messages.Add(ChatMessage.ToolResult(call.Id, call.Name, content));

					if (result.Images is { Count: > 0 })
					{
						foreach (var image in result.Images) _ui.OnSnapshot(image);
						snapshotsUsed++;
						snapshotImages.AddRange(result.Images);
					}
				}

				// 图片必须排在全部 tool 消息之后，否则会破坏 tool_calls 与 tool_call_id 的配对
				if (snapshotImages.Count > 0)
					Session.Messages.Add(ChatMessage.User("（系统：本轮 RenderSnapshot 生成的预览图）", snapshotImages));

				if (token.IsCancellationRequested)
				{
					_ui.OnStopped();
					return;
				}

				_store.Save(Session);
			}

			_ui.OnError($"本轮工具调用已达上限（{maxRounds} 轮），再发一条消息即可继续");
		}
		catch (OperationCanceledException)
		{
			_ui.OnStopped();
		}
		catch (Exception ex)
		{
			_ui.OnError(ex.Message);
		}
		finally
		{
			_store.Save(Session);
			Busy = false;
			_cts = null;
			_ui.OnBusyChanged(false);
		}
	}

	public void Cancel() => _cts?.Cancel();

	public string SaveAttachment(Image image) => _store.SaveAttachment(image);

	public Image LoadAttachment(string name) => _store.LoadAttachment(name);

	// tool_calls 必须被对应的 tool 消息紧跟，否则 API 直接 400。
	// 这里把预览图/计数消息挪到该轮 tool 消息之后，并给缺失的 tool_call 补一条占位结果（兼容中断与旧历史）。
	private static List<ChatMessage> SanitizeOrder(List<ChatMessage> source)
	{
		var result = new List<ChatMessage>(source.Count);
		int i = 0;
		while (i < source.Count)
		{
			var message = source[i++];
			result.Add(message);
			if (message.Role != "assistant" || message.ToolCalls is not { Count: > 0 }) continue;

			var names = new Dictionary<string, string>();
			var pending = new HashSet<string>();
			foreach (var call in message.ToolCalls)
			{
				names[call.Id] = call.Name;
				pending.Add(call.Id);
			}

			var deferred = new List<ChatMessage>();
			while (i < source.Count && pending.Count > 0)
			{
				var next = source[i];
				if (next.Role == "tool" && next.ToolCallId != null && pending.Remove(next.ToolCallId))
				{
					result.Add(next);
					i++;
				}
				else if (IsSnapshotNote(next))
				{
					deferred.Add(next);
					i++;
				}
				else
				{
					break;
				}
			}

			foreach (var id in pending)
				result.Add(ChatMessage.ToolResult(id, names.GetValueOrDefault(id, ""), "（该工具调用未执行）"));
			result.AddRange(deferred);
		}
		return result;
	}

	private static bool IsSnapshotNote(ChatMessage message) =>
		message.Role is "user" or "system" &&
		(message.Content.StartsWith("（系统：本轮 RenderSnapshot") ||
		 message.Content.StartsWith("系统：本轮剩余截图次数") ||
		 message.Content.StartsWith("系统：本轮截图次数已用完"));

	private string BuildSystemPrompt()
	{
		string prompt = Godot.FileAccess.GetFileAsString(PromptPath);
		if (string.IsNullOrWhiteSpace(prompt))
		{
			GD.PushError($"提示词文件缺失：{PromptPath}");
			return "你是 AIVoxel Studio 的体素建模助手。";
		}
		return prompt;
	}
}
