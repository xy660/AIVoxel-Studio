using Godot;
using System.Text.Json.Nodes;
using AIVoxelStudio.Core;
using AIVoxelStudio.Core.Mc;

namespace AIVoxelStudio.AI;

public sealed class ToolResult
{
	public bool Ok { get; init; }
	public string Content { get; init; } = "";
	public List<string> Images { get; init; }

	public static ToolResult Success(string content, List<string> images = null) =>
		new() { Ok = true, Content = content, Images = images };
	public static ToolResult Failure(string content) => new() { Ok = false, Content = content };
}

public class ToolContext
{
	public Func<Vector3I> PlayerBlock { get; init; } = () => Vector3I.Zero;
	public Func<string> SelectedBlock { get; init; } = () => "";
	public Func<int> RegionCount { get; init; } = () => 0;
	public Func<Vector3, Vector3, Task<Image>> RenderSnapshot { get; init; }
	public Func<Image, string> SaveAttachment { get; init; }
	public float BlockMeters { get; init; } = 1f;
	public string ModeName { get; init; } = "";
}

public sealed class VoxelToolContext : ToolContext
{
	public World World { get; init; }
	public VoxelEditService Editor { get; init; }
	public Func<BlockInfo> SelectedBlockInfo { get; init; } = () => BlockInfo.Air;
}

public sealed class McToolContext : ToolContext
{
	public McWorld World { get; init; }
	public McAssets Assets { get; init; }
}

public interface ITool
{
	string Name { get; }
	string Description { get; }
	JsonObject ParametersSchema { get; }
	Task<ToolResult> ExecuteAsync(JsonObject args, ToolContext context);
}

public sealed class ToolRegistry
{
	private readonly Dictionary<string, ITool> _tools = new(StringComparer.OrdinalIgnoreCase);

	public void RegisterTool(ITool tool)
	{
		if (tool == null || string.IsNullOrWhiteSpace(tool.Name))
			throw new ArgumentException("Tool must have a name");
		_tools[tool.Name] = tool;
	}

	public bool UnregisterTool(string name) => _tools.Remove(name);

	public ITool GetTool(string name) => _tools.GetValueOrDefault(name);

	public IReadOnlyCollection<ITool> GetAllTools() => _tools.Values;

	public JsonArray BuildSchemas()
	{
		var array = new JsonArray();
		foreach (var tool in _tools.Values)
		{
			array.Add(new JsonObject
			{
				["type"] = "function",
				["function"] = new JsonObject
				{
					["name"] = tool.Name,
					["description"] = tool.Description,
					["parameters"] = tool.ParametersSchema.DeepClone(),
				},
			});
		}
		return array;
	}

	public async Task<ToolResult> InvokeAsync(string name, string argumentsJson, ToolContext context)
	{
		var tool = GetTool(name);
		if (tool == null) return ToolResult.Failure($"未知工具: {name}");

		JsonObject args;
		try
		{
			args = string.IsNullOrWhiteSpace(argumentsJson)
				? new JsonObject()
				: JsonNode.Parse(argumentsJson) as JsonObject ?? new JsonObject();
		}
		catch (Exception)
		{
			return ToolResult.Failure($"工具参数不是合法 JSON: {argumentsJson}");
		}

		try
		{
			return await tool.ExecuteAsync(args, context);
		}
		catch (Exception ex)
		{
			return ToolResult.Failure($"{name} 执行异常: {ex.Message}");
		}
	}
}
