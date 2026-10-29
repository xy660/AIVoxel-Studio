using Godot;
using AIVoxelStudio.Core;

namespace AIVoxelStudio.UI;

public partial class NewSaveDialog : ConfirmationDialog
{
    private LineEdit _name;
    private OptionButton _kind;
    private CheckBox _ground;

    public override void _Ready()
    {
        _name = GetNode<LineEdit>("Margin/Box/NameInput");
        _kind = GetNode<OptionButton>("Margin/Box/KindRow/KindOption");
        _ground = GetNode<CheckBox>("Margin/Box/GroundCheck");
    }

    public string SaveNameText => _name.Text;

    public BuildKind Kind => _kind.Selected == 1 ? BuildKind.Minecraft : BuildKind.Voxel;

    public bool GenerateGround => _ground.ButtonPressed;

    public void ResetInputs()
    {
        _name.Text = "";
        _kind.Selected = 0;
        _ground.ButtonPressed = true;
    }

    public void FocusName() => _name.GrabFocus();
}
