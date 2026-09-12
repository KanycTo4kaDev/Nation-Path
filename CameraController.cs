using Godot;

public partial class CameraController : Camera2D
{
    [Export] public float ZoomSpeed = 0.1f;
    [Export] public float MinZoom = 0.3f;
    [Export] public float MaxZoom = 3.0f;

    [Export] public float CamLimitLeft = 100f;
    [Export] public float CamLimitRight = 1180f;
    [Export] public float CamLimitTop = -100f;
    [Export] public float CamLimitBottom = 820f;

    private bool _isPanning = false;
    private Vector2 _lastMousePos;
    private float _trauma = 0f;

    // Блокировка движения камеры (напр. открыты исследования).
    public bool InputLocked = false;

    public void AddShake(float amount)
    {
        _trauma = Mathf.Min(1f, _trauma + amount);
    }

    public override void _Ready()
    {
        MakeCurrent();
        Position = GetViewportRect().Size / 2f;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed && !InputLocked)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp)
                ZoomBy(ZoomSpeed);
            else if (mb.ButtonIndex == MouseButton.WheelDown)
                ZoomBy(-ZoomSpeed);
        }

        if (@event is InputEventKey ek && ek.Pressed && ek.Keycode == Key.F11)
        {
            if (GetWindow().Mode == Window.ModeEnum.Fullscreen)
                GetWindow().Mode = Window.ModeEnum.Windowed;
            else
                GetWindow().Mode = Window.ModeEnum.Fullscreen;
        }
    }

    public override void _Process(double delta)
    {
        if (Input.IsMouseButtonPressed(MouseButton.Right) && !InputLocked)
        {
            Vector2 mousePos = GetViewport().GetMousePosition();
            if (_isPanning)
            {
                Vector2 panDelta = (mousePos - _lastMousePos) / Zoom;
                Position -= panDelta;
                Position = ClampPosition(Position);
            }
            _isPanning = true;
            _lastMousePos = mousePos;
        }
        else
        {
            _isPanning = false;
        }

        if (Input.IsKeyPressed(Key.F) && !InputLocked)
        {
            Position = ClampPosition(GetViewportRect().Size / 2f);
            Zoom = Vector2.One;
        }

        if (_trauma > 0f)
        {
            _trauma = Mathf.Max(0f, _trauma - (float)delta * 1.5f);
            Offset = new Vector2((float)GD.RandRange(-1.0, 1.0), (float)GD.RandRange(-1.0, 1.0)) * _trauma * 12f;
        }
        else
        {
            Offset = Vector2.Zero;
        }
    }

    private void ZoomBy(float amount)
    {
        float z = Mathf.Clamp(Zoom.X + amount, MinZoom, MaxZoom);
        Zoom = new Vector2(z, z);
    }

    private Vector2 ClampPosition(Vector2 pos)
    {
        return new Vector2(
            Mathf.Clamp(pos.X, CamLimitLeft, CamLimitRight),
            Mathf.Clamp(pos.Y, CamLimitTop, CamLimitBottom)
        );
    }
}
