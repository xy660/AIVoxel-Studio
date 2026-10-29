using System.Text.Json.Nodes;

namespace AIVoxelStudio.AI.Tools;

// 向用户提问：弹窗给出选项，用户也可选「其他」自行输入；支持一次提多个问题
internal sealed class AskUserTool : ToolBase
{
    private const int MaxQuestions = 4;
    private const int MaxOptions = 8;

    public override string Name => "AskUser";
    public override string Description =>
        "向用户提问并等待回答。用户面前会弹出选择题，每题除你给的 items 外始终有「其他：____」可自行输入。" +
        "一次可提 1~4 个问题，用 questions 数组传入，每项 {q: 问题文本, items: [选项...]}。" +
        "适合在信息不足时确认位置、尺寸、风格、颜色等关键选择，不要拿它确认已经明确的信息。" +
        "返回用户选择的文本；用户跳过时返回跳过提示。";

    public override JsonObject ParametersSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["questions"] = new JsonObject
            {
                ["type"] = "array",
                ["description"] = $"要问的问题，按顺序展示，最多 {MaxQuestions} 个",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["q"] = new JsonObject { ["type"] = "string", ["description"] = "问题文本" },
                        ["items"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["description"] = $"选项文本，最多 {MaxOptions} 个",
                            ["items"] = new JsonObject { ["type"] = "string" },
                        },
                    },
                    ["required"] = new JsonArray("q", "items"),
                },
            },
        },
        ["required"] = new JsonArray("questions"),
    };

    protected override async Task<ToolResult> RunAsync(JsonObject args, ToolContext context)
    {
        if (context.AskUser == null) return ToolResult.Failure("当前环境不支持向用户提问");

        var questions = Parse(args, out string error);
        if (questions == null) return ToolResult.Failure(error);

        var answers = await context.AskUser(questions);
        if (answers == null || answers.Count == 0)
            return ToolResult.Success("用户跳过了本次提问（未作答），请按合理默认方案继续，并在回答里说明你采用的假设。");

        if (questions.Count == 1)
        {
            string only = answers.Count > 0 ? answers[0] : "";
            return ToolResult.Success(only.Length == 0
                ? "用户未回答该问题，请按合理默认方案继续。"
                : $"用户选择：{only}");
        }

        var lines = new List<string>();
        for (int i = 0; i < questions.Count; i++)
        {
            string answer = i < answers.Count ? answers[i] : "";
            if (answer.Length == 0) answer = "（未回答）";
            lines.Add($"{i + 1}. {questions[i].Prompt} → {answer}");
        }
        return ToolResult.Success("用户的回答：\n" + string.Join("\n", lines));
    }

    private static List<AskQuestion> Parse(JsonObject args, out string error)
    {
        error = null;
        if (args["questions"] is not JsonArray array || array.Count == 0)
        {
            error = "questions 必须是非空数组，每项形如 {{q: \"问题\", items: [\"选项1\", \"选项2\"]}}";
            return null;
        }
        if (array.Count > MaxQuestions)
        {
            error = $"一次最多提 {MaxQuestions} 个问题";
            return null;
        }

        var questions = new List<AskQuestion>();
        for (int i = 0; i < array.Count; i++)
        {
            if (array[i] is not JsonObject node)
            {
                error = $"第 {i + 1} 个问题格式不正确，必须是对象 {{q: \"问题\", items: [...]}}";
                return null;
            }

            string prompt = ReadString(node["q"])?.Trim();
            if (string.IsNullOrEmpty(prompt))
            {
                error = $"第 {i + 1} 个问题缺少 q（问题文本）";
                return null;
            }

            var options = new List<string>();
            if (node["items"] is JsonArray items)
            {
                foreach (var item in items)
                {
                    string text = ReadString(item)?.Trim();
                    if (!string.IsNullOrEmpty(text)) options.Add(text);
                }
            }
            if (options.Count > MaxOptions)
            {
                error = $"第 {i + 1} 个问题的选项最多 {MaxOptions} 个";
                return null;
            }

            questions.Add(new AskQuestion { Prompt = prompt, Options = options });
        }
        return questions;
    }

    private static string ReadString(JsonNode node)
    {
        try
        {
            return node is JsonValue value ? value.GetValue<string>() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
