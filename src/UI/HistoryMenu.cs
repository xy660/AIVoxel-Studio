using Godot;
using AIVoxelStudio.AI;

namespace AIVoxelStudio.UI;

// 历史对话列表：每行一个按钮，右键在行上直接触发删除（PopupMenu 在嵌入式窗口里拿不到悬停项）
public partial class HistoryMenu : PopupPanel
{
    public event Action<int> SessionChosen;
    public event Action<int> SessionDeleteRequested;

    private VBoxContainer _items;

    public override void _Ready()
    {
        _items = GetNode<VBoxContainer>("Margin/Items");
    }

    public void ShowSessions(IReadOnlyList<ChatSessionInfo> sessions, string activeId)
    {
        foreach (var child in _items.GetChildren())
        {
            _items.RemoveChild(child);
            child.QueueFree();
        }

        if (sessions.Count == 0)
        {
            _items.AddChild(MakeLabel("暂无对话"));
            return;
        }

        for (int i = 0; i < sessions.Count; i++)
        {
            var info = sessions[i];
            string title = string.IsNullOrWhiteSpace(info.Title) ? "新对话" : info.Title;
            if (title.Length > 18) title = title[..18] + "…";
            string time = DateTimeOffset.FromUnixTimeMilliseconds(info.UpdatedAt).ToLocalTime().ToString("MM-dd HH:mm");
            string text = $"{title}   {time}{(info.Id == activeId ? "   ● 当前" : "")}";
            _items.AddChild(MakeRow(text, i));
        }
    }

    public Vector2I ContentSize()
    {
        var min = _items.GetCombinedMinimumSize();
        return new Vector2I((int)Mathf.Max(240, min.X), (int)Mathf.Max(32, min.Y));
    }

    private Control MakeRow(string text, int index)
    {
        var button = new Button
        {
            Text = text,
            Flat = true,
            Alignment = HorizontalAlignment.Left,
            ClipText = true,
            CustomMinimumSize = new Vector2(320, 30),
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };

        button.Pressed += () =>
        {
            Hide();
            SessionChosen?.Invoke(index);
        };
        button.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }) return;
            button.AcceptEvent();
            SessionDeleteRequested?.Invoke(index);
        };

        return button;
    }

    private static Control MakeLabel(string text)
    {
        return new Label
        {
            Text = text,
            CustomMinimumSize = new Vector2(320, 30),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }
}
