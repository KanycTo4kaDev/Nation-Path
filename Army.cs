using Godot;

public partial class Army : Node2D
{
    public int Id;
    public int RegionId;
    public int Soldiers;
    public int HP;
    public int MaxHP = 100;
    public int PlayerId;
    public UnitType Type = UnitType.Infantry;
    public bool IsSelected;
    public bool IsWounded;
    private float _regenAccum;
    private float _flashTimer = 0f;
    private const float FlashDuration = 0.25f;
    public const int CriticalHp = 25;

    private static Texture2D _helmetTex;

    public void Initialize(int id, int regionId, int playerId, int soldiers, UnitType type = UnitType.Infantry)
    {
        Id = id;
        RegionId = regionId;
        PlayerId = playerId;
        Soldiers = soldiers;
        Type = type;
        HP = 100;
        MaxHP = 100;
        IsSelected = false;

        if (_helmetTex == null)
        {
            // Через пайплайн импорта (GD.Load резолвит remap в .ctex) —
            // работает и в редакторе, и в экспорте. LoadFromFile исходника
            // в экспорте падает: исходника нет в PCK.
            _helmetTex = GD.Load<Texture2D>("res://Assets/helmet.jpg");
        }
    }

    public float GetRadius()
    {
        return 14f;
    }

    public void Regenerate(float delta)
    {
        if (!IsWounded) return;
        _regenAccum += GameManager.RegenRate(PlayerId) * delta;
        while (_regenAccum >= 1f)
        {
            HP += 1;
            _regenAccum -= 1f;
        }
        if (HP >= MaxHP)
        {
            IsWounded = false;
            _regenAccum = 0f;
            HP = MaxHP;
        }
        QueueRedraw();
    }

    public void FlashHit()
    {
        _flashTimer = FlashDuration;
    }

    public override void _Process(double delta)
    {
        if (_flashTimer > 0f)
        {
            _flashTimer -= (float)delta;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var nation = GetNodeOrNull<MapGenerator>("/root/Main")?.GetNationById(PlayerId);
        Color color = nation != null ? nation.Color : Colors.Gray;
        if (Type == UnitType.Elite)
            color = color.Lightened(0.25f);
        float s = GetRadius();

        Texture2D iconTex = Type switch
        {
            UnitType.Militia => GameIcons.Get("militia"),
            UnitType.Elite => GameIcons.Get("elite"),
            _ => _helmetTex,
        };

        if (iconTex != null)
        {
            float size = s * 1.8f;
            var rect = new Rect2(new Vector2(-size / 2f, -size / 2f), new Vector2(size, size));

            // Фирменное оформление как у фото шлема: тёмная подложка
            // в цвет нации + серая рамка. Одинаково у всех типов,
            // отличается только PNG внутри.
            DrawRect(rect, color.Darkened(0.45f));
            DrawRect(rect.Grow(1.5f), new Color(0.55f, 0.55f, 0.6f), false, 2f);

            for (int i = 0; i < 8; i++)
            {
                Vector2 offset = Vector2.FromAngle(Mathf.Tau * i / 8f) * 1.2f;
                DrawTextureRect(iconTex, new Rect2(rect.Position + offset, rect.Size), false, new Color(1f, 1f, 1f, 0.85f));
            }

            DrawTextureRect(iconTex, rect, false, color);
        }
        else
        {
            DrawCircle(Vector2.Zero, s, color);
        }

        if (IsSelected)
        {
            DrawArc(Vector2.Zero, s + 2f, 0, Mathf.Tau, 32, Colors.Yellow, 2f);
        }

        if (_flashTimer > 0f)
        {
            float alpha = Mathf.Clamp(_flashTimer / FlashDuration, 0f, 1f) * 0.55f;
            DrawCircle(Vector2.Zero, s + 1f, new Color(1f, 0.2f, 0.2f, alpha));
        }

        float barWidth = s * 2f;
        float barHeight = 3f;
        float barY = s + 3f;
        var barBg = new Rect2(-barWidth / 2f, barY, barWidth, barHeight);
        DrawRect(barBg, new Color(0.15f, 0.15f, 0.15f, 0.9f));

        float ratio = MaxHP > 0 ? (float)HP / MaxHP : 0f;
        Color barColor;
        if (ratio > 0.5f)
            barColor = Colors.Green;
        else if (ratio > 0.25f)
            barColor = Colors.Yellow;
        else
            barColor = Colors.Red;

        var barFill = new Rect2(-barWidth / 2f, barY, barWidth * ratio, barHeight);
        DrawRect(barFill, barColor);

        // Маркер низкого HP: горит при HP≤25, гаснет после восстановления выше.
        if (HP <= CriticalHp)
        {
            var square = new Rect2(barWidth / 2f + 4f, barY - 1f, 5f, 5f);
            DrawRect(square, Colors.Red);
        }

        var font = ThemeDB.FallbackFont;
        int fontSize = 10;
        string text = Soldiers.ToString();
        float iconSize = s * 1.8f;
        Vector2 textSize = font.GetStringSize(text, fontSize: fontSize);
        Vector2 textPos = new Vector2(iconSize / 2f - textSize.X - 1f, iconSize / 2f + 2f);
        DrawStringOutline(font, textPos, text, HorizontalAlignment.Left, -1, fontSize, 3, new Color(0f, 0f, 0f, 0.8f));
        DrawString(font, textPos, text, fontSize: fontSize, modulate: Colors.White);
    }
}
