using System.Text.Json.Nodes;

namespace AIVoxelStudio.AI;

public sealed class LlmConfig
{
	public string BaseUrl { get; set; } = "";
	public string ApiKey { get; set; } = "";
	public string Model { get; set; } = "";
	public float Temperature { get; set; } = 0.7f;
	/// <summary>思考强度，空字符串表示关闭思考。</summary>
	public string ReasoningEffort { get; set; } = "";
	/// <summary>额外请求参数（JSON 对象文本），合并进请求体，用于供应商特有的开关。</summary>
	public string ExtraBodyJson { get; set; } = "";
}

public sealed class ToolCall
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public string Arguments { get; set; } = "";
}

public sealed class ChatMessage
{
	public string Role { get; set; } = "user";
	public string Content { get; set; } = "";
	public List<ToolCall> ToolCalls { get; set; }
	public string ToolCallId { get; set; }
	public string Name { get; set; }
	public List<string> Images { get; set; }

	public static ChatMessage User(string text, List<string> images = null) =>
		new() { Role = "user", Content = text, Images = images is { Count: > 0 } ? images : null };

	public static ChatMessage Assistant(string text, List<ToolCall> calls = null) =>
		new() { Role = "assistant", Content = text, ToolCalls = calls };

	public static ChatMessage ToolResult(string callId, string name, string content) =>
		new() { Role = "tool", ToolCallId = callId, Name = name, Content = content };

	public static ChatMessage System(string text) => new() { Role = "system", Content = text };

	public JsonObject ToJson(bool forRequest = false, Func<string, string> imageLoader = null)
	{
		var obj = new JsonObject
		{
			["role"] = Role,
		};

		var imageUrls = new List<string>();
		if (forRequest && imageLoader != null && Images is { Count: > 0 })
		{
			foreach (var image in Images)
			{
				string url = imageLoader(image);
				if (!string.IsNullOrEmpty(url)) imageUrls.Add(url);
			}
		}

		if (imageUrls.Count > 0)
		{
			var parts = new JsonArray();
			if (!string.IsNullOrEmpty(Content))
				parts.Add(new JsonObject { ["type"] = "text", ["text"] = Content });
			foreach (var url in imageUrls)
				parts.Add(new JsonObject
				{
					["type"] = "image_url",
					["image_url"] = new JsonObject { ["url"] = url },
				});
			obj["content"] = parts;
		}
		else
		{
			obj["content"] = Content;
		}

		if (!forRequest && Images is { Count: > 0 })
		{
			var images = new JsonArray();
			foreach (var image in Images) images.Add(image);
			obj["images"] = images;
		}

		if (ToolCalls is { Count: > 0 })
		{
			var calls = new JsonArray();
			foreach (var call in ToolCalls)
			{
				calls.Add(new JsonObject
				{
					["id"] = call.Id,
					["type"] = "function",
					["function"] = new JsonObject
					{
						["name"] = call.Name,
						["arguments"] = call.Arguments,
					},
				});
			}
			obj["tool_calls"] = calls;
		}
		if (!string.IsNullOrEmpty(ToolCallId)) obj["tool_call_id"] = ToolCallId;
		if (!string.IsNullOrEmpty(Name)) obj["name"] = Name;
		return obj;
	}

	public static ChatMessage FromJson(JsonObject obj)
	{
		var message = new ChatMessage
		{
			Role = obj["role"]?.GetValue<string>() ?? "assistant",
			Content = obj["content"]?.GetValue<string>() ?? "",
			ToolCallId = obj["tool_call_id"]?.GetValue<string>(),
			Name = obj["name"]?.GetValue<string>(),
		};

		if (obj["tool_calls"] is JsonArray calls)
		{
			message.ToolCalls = new List<ToolCall>();
			foreach (var node in calls)
			{
				if (node is not JsonObject call) continue;
				message.ToolCalls.Add(new ToolCall
				{
					Id = call["id"]?.GetValue<string>() ?? "",
					Name = call["function"]?["name"]?.GetValue<string>() ?? "",
					Arguments = call["function"]?["arguments"]?.GetValue<string>() ?? "{}",
				});
			}
		}

		if (obj["images"] is JsonArray images)
		{
			message.Images = new List<string>();
			foreach (var node in images)
			{
				string name = node?.GetValue<string>();
				if (!string.IsNullOrWhiteSpace(name)) message.Images.Add(name);
			}
			if (message.Images.Count == 0) message.Images = null;
		}

		return message;
	}
}

public sealed class ChatSession
{
	public string Id { get; set; } = "";
	public string Title { get; set; } = "新对话";
	public long UpdatedAt { get; set; }

	public List<ChatMessage> Messages { get; set; } = new();

	public JsonObject ToJson()
	{
		var messages = new JsonArray();
		foreach (var message in Messages) messages.Add(message.ToJson());
		return new JsonObject
		{
			["id"] = Id,
			["title"] = Title,
			["updatedAt"] = UpdatedAt,
			["messages"] = messages,
		};
	}

	public static ChatSession FromJson(JsonObject obj)
	{
		var session = new ChatSession
		{
			Id = obj["id"]?.GetValue<string>() ?? "",
			Title = obj["title"]?.GetValue<string>() ?? "新对话",
			UpdatedAt = obj["updatedAt"]?.GetValue<long>() ?? 0,
		};

		if (obj["messages"] is JsonArray messages)
		{
			foreach (var node in messages)
			{
				if (node is JsonObject message) session.Messages.Add(ChatMessage.FromJson(message));
			}
		}
		return session;
	}
}
