using Godot;
using AIVoxelStudio.Core.Abstractions;
using AIVoxelStudio.Render;

namespace AIVoxelStudio.Core.Selection;

// 通用选区模式：两次左键选对角点，Y 确认、N/Esc 取消
public sealed partial class RegionSelector : Node3D
{
    public Func<Camera3D> CameraSource { get; init; }
    public Func<Vector3, Vector3, Vector3I?> Pick { get; init; }
    public Func<Vector3I> PlayerCellSource { get; init; }
    public float BlockMeters { get; init; } = 1f;

    private Action<RegionBox> _callback;
    private Action _cancelled;
    private RegionGuide _guide;
    private Vector3I? _first;
    private Vector3I? _second;

    public bool Active { get; private set; }

    public void Enter(Action<RegionBox> callback, Action cancelled = null)
    {
        if (Active) return;

        Active = true;
        _callback = callback;
        _cancelled = cancelled;
        _first = null;
        _second = null;

        _guide = new RegionGuide(BlockMeters);
        AddChild(_guide);
    }

    public void Cancel()
    {
        if (!Active) return;

        var cancelled = _cancelled;
        End();
        cancelled?.Invoke();
    }

    private void Confirm()
    {
        if (_first is not { } first) return;

        var box = RegionBox.FromCorners(first, _second ?? first);
        var callback = _callback;
        End();
        callback?.Invoke(box);
    }

    private void End()
    {
        Active = false;
        _callback = null;
        _cancelled = null;
        _first = null;
        _second = null;

        if (_guide != null && IsInstanceValid(_guide))
        {
            _guide.QueueFree();
            _guide = null;
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (!Active) return;

        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            switch (key.Keycode)
            {
                case Key.Y:
                case Key.Enter:
                    Confirm();
                    GetViewport().SetInputAsHandled();
                    break;
                case Key.N:
                case Key.Escape:
                    Cancel();
                    GetViewport().SetInputAsHandled();
                    break;
                case Key.Q:
                    UsePlayerCell();
                    GetViewport().SetInputAsHandled();
                    break;
            }
            return;
        }

        // 选区期间吞掉鼠标点击，避免顺带破坏/放置方块
        if (@event is InputEventMouseButton mouse)
        {
            GetViewport().SetInputAsHandled();
            if (!mouse.Pressed || mouse.ButtonIndex != MouseButton.Left) return;

            var camera = CameraSource?.Invoke();
            if (camera == null) return;

            var origin = camera.GlobalPosition / BlockMeters;
            var direction = -camera.GlobalTransform.Basis.Z;
            var cell = Pick?.Invoke(origin, direction);
            if (cell is not { } picked) return;

            SetCorner(picked);
        }
    }

    // Q：直接用玩家脚下的方块作为角点，避免瞄准空格子时选不中
    private void UsePlayerCell()
    {
        if (PlayerCellSource?.Invoke() is not { } cell) return;
        SetCorner(cell);
    }

    private void SetCorner(Vector3I cell)
    {
        if (_first == null)
        {
            _first = cell;
        }
        else if (_second == null)
        {
            _second = cell;
        }
        else
        {
            _first = cell;
            _second = null;
        }

        _guide?.SetCorners(_first, _second);
    }
}
