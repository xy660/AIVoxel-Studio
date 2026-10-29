using Godot;
using AIVoxelStudio.AI;

namespace AIVoxelStudio.UI;

/// <summary>AI 提问框：每题列出选项，另始终提供「其他：____」自由输入；支持一次多题。</summary>
public partial class AskUserDialog : Window
{
    private VBoxContainer _questions;
    private ScrollContainer _scroll;
    private Button _submit;
    private Button _skip;
    private readonly List<QuestionState> _states = new();
    private TaskCompletionSource<IReadOnlyList<string>> _completion;

    public override void _Ready()
    {
        Theme = UiTheme.Create();
        _questions = GetNode<VBoxContainer>("Root/VBox/Scroll/Questions");
        _scroll = GetNode<ScrollContainer>("Root/VBox/Scroll");
        _submit = GetNode<Button>("Root/VBox/Footer/SubmitButton");
        _skip = GetNode<Button>("Root/VBox/Footer/SkipButton");
        _submit.Pressed += Submit;
        _skip.Pressed += Skip;
        CloseRequested += Skip;
    }

    /// <summary>展示问题并等待用户作答；跳过或关闭窗口返回空列表。</summary>
    public Task<IReadOnlyList<string>> AskAsync(IReadOnlyList<AskQuestion> questions)
    {
        Build(questions);
        _completion = new TaskCompletionSource<IReadOnlyList<string>>();
        PopupCentered(new Vector2I(600, 480));
        return _completion.Task;
    }

    /// <summary>AI 生成被中止时关闭弹窗，按跳过处理。</summary>
    public void CancelPending() => Skip();

    private void Build(IReadOnlyList<AskQuestion> questions)
    {
        foreach (var child in _questions.GetChildren())
        {
            _questions.RemoveChild(child);
            child.QueueFree();
        }
        _states.Clear();

        for (int i = 0; i < questions.Count; i++)
        {
            var question = questions[i];
            var panel = new PanelContainer();
            var style = new StyleBoxFlat
            {
                BgColor = new Color(0.12f, 0.13f, 0.16f, 0.92f),
                BorderColor = new Color(1f, 1f, 1f, 0.1f),
                ContentMarginLeft = 10,
                ContentMarginRight = 10,
                ContentMarginTop = 8,
                ContentMarginBottom = 8,
            };
            style.SetBorderWidthAll(1);
            style.SetCornerRadiusAll(6);
            panel.AddThemeStyleboxOverride("panel", style);

            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 6);
            panel.AddChild(box);

            string title = questions.Count > 1 ? $"{i + 1}. {question.Prompt}" : question.Prompt;
            var label = new Label
            {
                Text = title,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            label.AddThemeFontSizeOverride("font_size", 15);
            box.AddChild(label);

            var state = new QuestionState();
            foreach (var option in question.Options)
            {
                var choice = new Button
                {
                    Text = option,
                    ToggleMode = true,
                    ButtonGroup = state.Group,
                    Alignment = HorizontalAlignment.Left,
                    FocusMode = Control.FocusModeEnum.None,
                };
                choice.Toggled += pressed =>
                {
                    if (pressed) state.ClearCustom();
                };
                box.AddChild(choice);
            }

            var customRow = new HBoxContainer();
            customRow.AddThemeConstantOverride("separation", 6);
            customRow.AddChild(new Label { Text = "其他：" });
            var edit = new LineEdit
            {
                PlaceholderText = "自行输入…",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            edit.TextChanged += text =>
            {
                if (!string.IsNullOrWhiteSpace(text)) state.ClearSelection();
            };
            customRow.AddChild(edit);
            state.Custom = edit;
            box.AddChild(customRow);

            _states.Add(state);
            _questions.AddChild(panel);
        }

        _scroll.ScrollVertical = 0;
    }

    private void Submit()
    {
        if (_completion == null) return;

        var answers = new List<string>(_states.Count);
        foreach (var state in _states) answers.Add(state.Answer());
        Resolve(answers);
    }

    private void Skip()
    {
        if (_completion == null) return;
        Resolve(Array.Empty<string>());
    }

    private void Resolve(IReadOnlyList<string> answers)
    {
        var completion = _completion;
        _completion = null;
        Hide();
        completion.TrySetResult(answers);
    }

    private sealed class QuestionState
    {
        public ButtonGroup Group { get; } = new();
        public LineEdit Custom { get; set; }

        public void ClearSelection()
        {
            var pressed = Group.GetPressedButton();
            if (pressed != null) pressed.SetPressedNoSignal(false);
        }

        public void ClearCustom()
        {
            if (Custom != null && Custom.Text.Length > 0) Custom.Text = "";
        }

        public string Answer()
        {
            string custom = Custom?.Text.Trim() ?? "";
            if (custom.Length > 0) return custom;
            return Group.GetPressedButton() is Button button ? button.Text : "";
        }
    }
}
