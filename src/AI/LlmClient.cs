using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Godot;

namespace AIVoxelStudio.AI;

public sealed class LlmClient
{
	private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

	public LlmConfig Config { get; set; } = new();

	public async Task<ChatMessage> CompleteStreamAsync(
		IReadOnlyList<ChatMessage> messages,
		JsonArray tools,
		Action<string> onContent,
		Action<string> onReasoning,
		Func<string, string> imageLoader = null,
		CancellationToken ct = default)
	{
		if (string.IsNullOrWhiteSpace(Config.BaseUrl))
			throw new InvalidOperationException("API 地址为空，请先在设置中填写");
		if (string.IsNullOrWhiteSpace(Config.Model))
			throw new InvalidOperationException("模型名称为空，请先在设置中填写");

		string url = Config.BaseUrl.Trim().TrimEnd('/');
		if (!url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
			url += "/chat/completions";

		var messageArray = new JsonArray();
		foreach (var message in messages) messageArray.Add(message.ToJson(true, imageLoader));

		// 每次重试都重建 body，节点不能重复挂到多个父节点上
		JsonObject BuildBody(Action<JsonObject> reasoning)
		{
			var json = new JsonObject
			{
				["model"] = Config.Model,
				["messages"] = messageArray.DeepClone(),
				["temperature"] = Config.Temperature,
				["stream"] = true,
			};
			if (tools is { Count: > 0 })
			{
				json["tools"] = tools.DeepClone();
				json["tool_choice"] = "auto";
			}
			reasoning?.Invoke(json);
			MergeExtraBody(json);
			return json;
		}

		// 不同供应商的思考开关字段不同，接口返回 400 时依次降级到下一组
		var variants = ReasoningVariants();
		HttpResponseMessage response = null;
		string lastError = null;

		for (int i = 0; i < variants.Count; i++)
		{
			using var request = MakeRequest(url, BuildBody(variants[i]));
			response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
			if (response.IsSuccessStatusCode)
			{
				if (i > 0) GD.PushWarning($"[LLM] 接口拒绝了前面的思考参数，已改用第 {i + 1} 组");
				break;
			}

			int status = (int)response.StatusCode;
			string errorText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
			lastError = $"HTTP {status}: {(errorText.Length > 500 ? errorText[..500] : errorText)}";
			response.Dispose();
			response = null;
			if (status != 400) throw new HttpRequestException(lastError);
		}

		if (response == null) throw new HttpRequestException(lastError ?? "接口拒绝了请求");
		using var responseScope = response;

		string mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
		if (!mediaType.Contains("event-stream", StringComparison.OrdinalIgnoreCase))
		{
			string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
			return ParseResponse(text);
		}

		var content = new StringBuilder();
		var calls = new List<ToolCall>();

		try
		{
			using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
			using var reader = new StreamReader(stream, Encoding.UTF8);
			while (true)
			{
				string line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
				if (line == null) break;
				if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

				string payload = line[5..].Trim();
				if (payload.Length == 0) continue;
				if (payload == "[DONE]") break;

				if (JsonNode.Parse(payload) is not JsonObject root) continue;
				if (root["error"] is JsonObject error)
					throw new InvalidOperationException($"API 错误: {GetString(error["message"])}");

				if (root["choices"] is not JsonArray choices || choices.Count == 0) continue;
				if (choices[0] is not JsonObject choice || choice["delta"] is not JsonObject delta) continue;

				string chunk = GetString(delta["content"]);
				if (!string.IsNullOrEmpty(chunk))
				{
					content.Append(chunk);
					onContent?.Invoke(chunk);
				}

				string thinking = GetString(delta["reasoning_content"]) ?? GetString(delta["reasoning"]);
				if (!string.IsNullOrEmpty(thinking)) onReasoning?.Invoke(thinking);

				if (delta["tool_calls"] is JsonArray deltas) AccumulateToolCalls(deltas, calls);
			}
		}
		catch (OperationCanceledException)
		{
			// 用户点击停止：保留已收到内容，丢弃未完成的工具调用
			calls.Clear();
		}

		var reply = new ChatMessage { Role = "assistant", Content = content.ToString() };
		if (calls.Count > 0)
		{
			for (int i = 0; i < calls.Count; i++)
			{
				if (string.IsNullOrEmpty(calls[i].Id)) calls[i].Id = $"call_{i}";
				if (string.IsNullOrWhiteSpace(calls[i].Arguments)) calls[i].Arguments = "{}";
			}
			reply.ToolCalls = calls;
		}
		return reply;
	}

	private static void AccumulateToolCalls(JsonArray deltas, List<ToolCall> calls)
	{
		foreach (var node in deltas)
		{
			if (node is not JsonObject call) continue;

			int index = call["index"] is JsonValue iv && iv.TryGetValue<int>(out var value) ? value : 0;
			while (calls.Count <= index) calls.Add(new ToolCall());
			var target = calls[index];

			string id = GetString(call["id"]);
			if (!string.IsNullOrEmpty(id)) target.Id = id;

			if (call["function"] is not JsonObject function) continue;
			string name = GetString(function["name"]);
			if (!string.IsNullOrEmpty(name)) target.Name = name;
			string args = GetString(function["arguments"]);
			if (!string.IsNullOrEmpty(args)) target.Arguments += args;
		}
	}

	// 思考参数候选：关闭 = 依次尝试各家关闭字段；非空 = 先发 reasoning_effort，失败则退回默认
	private List<Action<JsonObject>> ReasoningVariants()
	{
		string effort = Config.ReasoningEffort?.Trim() ?? "";
		if (effort.Length > 0)
		{
			var variants = new List<Action<JsonObject>> { body => body["reasoning_effort"] = effort };
			// DeepSeek 只支持 none/low/high/max，没有 medium；被拒时换 high 再试
			if (string.Equals(effort, "medium", StringComparison.OrdinalIgnoreCase))
				variants.Add(body => body["reasoning_effort"] = "high");
			variants.Add(_ => { });
			return variants;
		}

		// DeepSeek / 方舟 / 智谱：thinking.type=disabled，且不要与 reasoning_effort 混传
		return new List<Action<JsonObject>>
		{
			body => { body["thinking"] = new JsonObject { ["type"] = "disabled" }; },
			body => { body["enable_thinking"] = false; },
			body => { body["reasoning_effort"] = "none"; },
			_ => { },
		};
	}

	private HttpRequestMessage MakeRequest(string url, JsonObject body)
	{
		var request = new HttpRequestMessage(HttpMethod.Post, url)
		{
			Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
		};
		if (!string.IsNullOrWhiteSpace(Config.ApiKey))
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Config.ApiKey.Trim());
		return request;
	}

	// 供应商特有的开关交给用户自己填，比如 {"thinking":{"type":"disabled"}}
	private void MergeExtraBody(JsonObject body)
	{
		string json = Config.ExtraBodyJson?.Trim() ?? "";
		if (json.Length == 0) return;

		try
		{
			if (JsonNode.Parse(json) is JsonObject extra)
			{
				foreach (var pair in extra) body[pair.Key] = pair.Value?.DeepClone();
			}
		}
		catch (Exception ex)
		{
			GD.PushWarning($"额外请求参数不是合法 JSON，已忽略：{ex.Message}");
		}
	}

	private static ChatMessage ParseResponse(string responseText)
	{
		var root = JsonNode.Parse(responseText) as JsonObject
			?? throw new InvalidOperationException("API 返回了无法解析的内容");

		if (root["error"] is JsonObject error)
			throw new InvalidOperationException($"API 错误: {GetString(error["message"])}");

		if (root["choices"] is not JsonArray choices || choices.Count == 0)
			throw new InvalidOperationException("API 返回为空（无 choices）");

		if (choices[0] is not JsonObject choice || choice["message"] is not JsonObject message)
			throw new InvalidOperationException("API 返回缺少 message 字段");

		var reply = new ChatMessage
		{
			Role = "assistant",
			Content = GetString(message["content"]) ?? "",
		};

		if (message["tool_calls"] is JsonArray calls && calls.Count > 0)
		{
			reply.ToolCalls = new List<ToolCall>();
			foreach (var node in calls)
			{
				if (node is not JsonObject call) continue;
				reply.ToolCalls.Add(new ToolCall
				{
					Id = GetString(call["id"]) ?? $"call_{reply.ToolCalls.Count}",
					Name = GetString(call["function"]?["name"]) ?? "",
					Arguments = GetString(call["function"]?["arguments"]) ?? "{}",
				});
			}
		}

		return reply;
	}

	private static string GetString(JsonNode node) =>
		node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
