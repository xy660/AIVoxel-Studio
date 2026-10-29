using Godot;
using System.Text.Json.Nodes;
using AIVoxelStudio.Core;

namespace AIVoxelStudio.AI;

public sealed class ChatSessionInfo
{
	public string Id { get; set; } = "";
	public string Title { get; set; } = "新对话";
	public long UpdatedAt { get; set; }
}

public sealed class ChatStore
{
	private readonly string _dir;
	private readonly List<ChatSessionInfo> _index = new();

	public string ActiveId { get; private set; } = "";

	public IReadOnlyList<ChatSessionInfo> Sessions => _index;

	public ChatStore(string saveName)
	{
		_dir = SaveManager.SaveDir(saveName);
	}

	private string IndexPath => Path.Combine(_dir, "ChatHistoryIndex.json");

	private string SessionPath(string id) => Path.Combine(_dir, $"ChatHistory-{id}.json");

	private string AttachmentsDir => Path.Combine(_dir, "attachments");

	public string SaveAttachment(Image image)
	{
		if (image == null) return null;

		try
		{
			Directory.CreateDirectory(AttachmentsDir);
			string name = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Random.Shared.Next(1000, 9999)}.png";
			File.WriteAllBytes(Path.Combine(AttachmentsDir, name), image.SavePngToBuffer());
			return name;
		}
		catch (Exception ex)
		{
			GD.PushWarning($"Failed to save attachment: {ex.Message}");
			return null;
		}
	}

	public Image LoadAttachment(string name)
	{
		if (string.IsNullOrWhiteSpace(name)) return null;
		string path = Path.Combine(AttachmentsDir, Path.GetFileName(name));
		if (!File.Exists(path)) return null;
		return Image.LoadFromFile(path);
	}

	public string AttachmentDataUrl(string name)
	{
		if (string.IsNullOrWhiteSpace(name)) return null;
		string path = Path.Combine(AttachmentsDir, Path.GetFileName(name));
		if (!File.Exists(path)) return null;

		// 直接读取已保存的字节，避免每次请求重新编码导致内容变化、缓存失效
		return "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(path));
	}

	public void LoadIndex()
	{
		_index.Clear();
		ActiveId = "";
		if (!File.Exists(IndexPath)) return;

		try
		{
			var root = JsonNode.Parse(File.ReadAllText(IndexPath)) as JsonObject;
			if (root?["sessions"] is JsonArray sessions)
			{
				foreach (var node in sessions)
				{
					if (node is not JsonObject obj) continue;
					_index.Add(new ChatSessionInfo
					{
						Id = obj["id"]?.GetValue<string>() ?? "",
						Title = obj["title"]?.GetValue<string>() ?? "新对话",
						UpdatedAt = obj["updatedAt"]?.GetValue<long>() ?? 0,
					});
				}
			}
			ActiveId = root?["activeId"]?.GetValue<string>() ?? "";
		}
		catch (Exception ex)
		{
			GD.PushWarning($"Failed to load chat index: {ex.Message}");
		}
	}

	public ChatSession LoadActiveOrLatest()
	{
		if (!string.IsNullOrEmpty(ActiveId) && File.Exists(SessionPath(ActiveId)))
		{
			var active = Load(ActiveId);
			if (active != null) return active;
		}

		var latest = _index.OrderByDescending(s => s.UpdatedAt).FirstOrDefault();
		if (latest != null)
		{
			var session = Load(latest.Id);
			if (session != null) return session;
		}

		return CreateSession();
	}

	public ChatSession CreateSession()
	{
		var session = new ChatSession
		{
			Id = $"chat-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Random.Shared.Next(100, 999)}",
			Title = "新对话",
			UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
		};
		Save(session);
		return session;
	}

	public ChatSession Load(string id)
	{
		if (string.IsNullOrWhiteSpace(id)) return null;

		try
		{
			var path = SessionPath(id);
			if (!File.Exists(path)) return null;
			var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
			return root == null ? null : ChatSession.FromJson(root);
		}
		catch (Exception ex)
		{
			GD.PushWarning($"Failed to load chat session {id}: {ex.Message}");
			return null;
		}
	}

	public void Save(ChatSession session)
	{
		if (session == null || string.IsNullOrWhiteSpace(session.Id)) return;

		session.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		try
		{
			Directory.CreateDirectory(_dir);
			string path = SessionPath(session.Id);
			string temp = path + ".tmp";
			File.WriteAllText(temp, session.ToJson().ToJsonString());
			if (File.Exists(path)) File.Delete(path);
			File.Move(temp, path);
		}
		catch (Exception ex)
		{
			GD.PushWarning($"Failed to save chat session {session.Id}: {ex.Message}");
		}

		var entry = _index.FirstOrDefault(s => s.Id == session.Id);
		if (entry == null)
		{
			_index.Add(new ChatSessionInfo { Id = session.Id, Title = session.Title, UpdatedAt = session.UpdatedAt });
		}
		else
		{
			entry.Title = session.Title;
			entry.UpdatedAt = session.UpdatedAt;
		}
		ActiveId = session.Id;
		SaveIndex();
	}

	public void SetActive(string id)
	{
		ActiveId = id;
		SaveIndex();
	}

	public void Delete(string id)
	{
		try
		{
			var path = SessionPath(id);
			if (File.Exists(path)) File.Delete(path);
		}
		catch (Exception ex)
		{
			GD.PushWarning($"Failed to delete chat session {id}: {ex.Message}");
		}

		_index.RemoveAll(s => s.Id == id);
		if (ActiveId == id) ActiveId = "";
		SaveIndex();
	}

	private void SaveIndex()
	{
		var sessions = new JsonArray();
		foreach (var info in _index.OrderByDescending(s => s.UpdatedAt))
		{
			sessions.Add(new JsonObject
			{
				["id"] = info.Id,
				["title"] = info.Title,
				["updatedAt"] = info.UpdatedAt,
			});
		}

		var root = new JsonObject
		{
			["activeId"] = ActiveId,
			["sessions"] = sessions,
		};

		try
		{
			Directory.CreateDirectory(_dir);
			File.WriteAllText(IndexPath, root.ToJsonString());
		}
		catch (Exception ex)
		{
			GD.PushWarning($"Failed to save chat index: {ex.Message}");
		}
	}
}
