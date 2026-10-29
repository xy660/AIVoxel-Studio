using Godot;
using AIVoxelStudio.Core;
using AIVoxelStudio.Core.Abstractions;

namespace AIVoxelStudio.Player;

public partial class PlayerController : CharacterBody3D
{
	public const float WalkSpeed = 3.6f;
	public const float SprintSpeed = 5.5f;
	public const float FlySpeed = 4f;
	public const float FlySprintSpeed = 10f;
	public const float JumpVelocity = 4.8f;
	public const float Gravity = 9.8f;
	public const float Reach = 3f;
	public IPlayerWorld PlayerWorld { get; set; }
	public float BlockMeters { get; set; } = VoxelUnits.BlockMeters;
	public float ReachMeters { get; set; } = Reach;
	public bool InputEnabled { get; private set; } = true;
	public bool Flying { get; private set; }
	public float MouseSensitivity { get; set; } = 0.0022f;

	public Camera3D Camera => _camera;

	private Camera3D _camera;
	private Node3D _head;
	private CollisionShape3D _shape;
	private MeshInstance3D _highlight;
	private float _pitch;
	private Vector3I? _targetBlock;

	public override void _Ready()
	{
		CollisionLayer = 1;
		CollisionMask = 1;

		_shape = new CollisionShape3D
		{
			Shape = new CapsuleShape3D { Radius = 0.35f, Height = 1.8f },
			Position = new Vector3(0, 0.9f, 0),
		};
		AddChild(_shape);

		_head = new Node3D { Position = new Vector3(0, 1.62f, 0) };
		AddChild(_head);

		_camera = new Camera3D { Current = true, Fov = 75f, Near = 0.01f, Far = 600f };
		_head.AddChild(_camera);

		_highlight = new MeshInstance3D
		{
			TopLevel = true,
			Mesh = new BoxMesh { Size = Vector3.One * (BlockMeters * 1.02f) },
			MaterialOverride = new StandardMaterial3D
			{
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				AlbedoColor = new Color(1f, 1f, 1f, 0.28f),
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			},
			Visible = false,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(_highlight);

		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	// 飞行即无碰撞：开启后可以穿墙，关闭后恢复碰撞体积
	public void SetFlying(bool flying)
	{
		Flying = flying;
		if (_shape != null) _shape.Disabled = flying;
		Velocity = Vector3.Zero;
	}

	public void SetInputEnabled(bool enabled)
	{
		InputEnabled = enabled;
		Input.MouseMode = enabled ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
		if (!enabled)
		{
			Velocity = Vector3.Zero;
			_targetBlock = null;
			if (_highlight != null) _highlight.Visible = false;
		}
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (e is InputEventKey key)
		{
			HandleKey(key);
			return;
		}

		if (e is InputEventMouseMotion motion && InputEnabled && Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			float dx = motion.Relative.X * MouseSensitivity;
			float dy = motion.Relative.Y * MouseSensitivity;
			RotateY(-dx);
			_pitch = Mathf.Clamp(_pitch - dy, -1.54f, 1.54f);
			_head.Rotation = new Vector3(_pitch, 0, 0);
			return;
		}

		if (e is InputEventMouseButton button && button.Pressed)
		{
			if (!InputEnabled)
			{
				return;
			}

			if (Input.MouseMode != Input.MouseModeEnum.Captured && button.ButtonIndex == MouseButton.Left)
			{
				Input.MouseMode = Input.MouseModeEnum.Captured;
				GetViewport().SetInputAsHandled();
				return;
			}

			if (Input.MouseMode == Input.MouseModeEnum.Captured)
			{
				if (button.ButtonIndex == MouseButton.Left) BreakBlock();
				else if (button.ButtonIndex == MouseButton.Right) PlaceBlock();
				else if (button.ButtonIndex == MouseButton.Middle && _targetBlock is { } cell) PickBlock(cell);
			}
		}
	}

	private void HandleKey(InputEventKey e)
	{
		if (!e.Pressed || e.Echo) return;
		if (!InputEnabled) return;

		if (e.Keycode == Key.F)
		{
			SetFlying(!Flying);
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		UpdateTarget();

		if (!InputEnabled || PlayerWorld == null)
		{
			Velocity = Vector3.Zero;
			return;
		}

		float dt = (float)delta;
		var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
		var wish = new Vector3(input.X, 0, input.Y);
		wish = GlobalTransform.Basis * wish;
		wish.Y = 0;
		wish = wish.Normalized();

		bool sprinting = Input.IsActionPressed("sprint");
		float speed = Flying ? (sprinting ? FlySprintSpeed : FlySpeed) : (sprinting ? SprintSpeed : WalkSpeed);

		if (Flying)
		{
			float vertical = (Input.IsActionPressed("jump") ? 1f : 0f) - (Input.IsActionPressed("sprint") ? 1f : 0f);
			Velocity = wish * speed + new Vector3(0, vertical * speed * 0.75f, 0);
		}
		else
		{
			var velocity = Velocity;
			velocity.X = wish.X * speed;
			velocity.Z = wish.Z * speed;
			if (!IsOnFloor()) velocity.Y -= Gravity * dt;
			if (Input.IsActionJustPressed("jump") && IsOnFloor()) velocity.Y = JumpVelocity;
			Velocity = velocity;
		}

		MoveAndSlide();
	}

	private void UpdateTarget()
	{
		if (PlayerWorld == null || _camera == null) return;

		var origin = _camera.GlobalPosition / BlockMeters;
		var forward = -_camera.GlobalTransform.Basis.Z;
		if (VoxelRay.Cast(PlayerWorld, origin, forward, ReachMeters / BlockMeters, out var hit, out _))
		{
			_targetBlock = hit;
			_highlight.Visible = true;
			_highlight.GlobalPosition = new Vector3(hit.X + 0.5f, hit.Y + 0.5f, hit.Z + 0.5f) * BlockMeters;
		}
		else
		{
			_targetBlock = null;
			_highlight.Visible = false;
		}
	}

	private void BreakBlock()
	{
		if (_targetBlock is not { } hit) return;
		PlayerWorld?.BreakAt(hit);
	}

	private void PickBlock(Vector3I cell) => PlayerWorld?.TryPickBlock(cell);

	private void PlaceBlock()
	{
		if (PlayerWorld == null) return;

		var origin = _camera.GlobalPosition / BlockMeters;
		var forward = -_camera.GlobalTransform.Basis.Z;
		if (!VoxelRay.Cast(PlayerWorld, origin, forward, ReachMeters / BlockMeters, out _, out var cell)) return;
		if (IntersectsPlayer(cell)) return;
		PlayerWorld.TryPlaceAt(cell);
	}

	private bool IntersectsPlayer(Vector3I cell)
	{
		var min = GlobalPosition + new Vector3(-0.35f, 0, -0.35f);
		var max = GlobalPosition + new Vector3(0.35f, 1.8f, 0.35f);
		var blockMin = new Vector3(cell.X, cell.Y, cell.Z) * BlockMeters;
		var blockMax = blockMin + Vector3.One * BlockMeters;
		return min.X < blockMax.X && max.X > blockMin.X
			&& min.Y < blockMax.Y && max.Y > blockMin.Y
			&& min.Z < blockMax.Z && max.Z > blockMin.Z;
	}
}
