using Godot;
using System.Collections.Generic;

// Дерево исследований: квадратные узлы-техи (слоты под будущие текстуры)
// + линии связей по таблице GameManager.TechRequires.
public partial class TechTreeView : Control
{
    public System.Action<int> BuyPressed;

    private const int NodeSize = 76;
    private const int ColGap = 60;
    private const int RowYWar = 170;
    private const int RowYEco = 490;
    private const int LabelHeight = 44;
    private const float LaneDy = 140f;

    private readonly Dictionary<int, TextureButton> _nodes = new();
    private readonly Dictionary<int, Label> _captions = new();
    private readonly Dictionary<int, Vector2> _centers = new();
    private readonly Dictionary<int, Texture2D> _placeholderCache = new();

    private int _nation = -1;
    private int _gold;
    private int _pts;
    private bool _god;
    private bool _observer;
    private PanelContainer _tip;
    private Label _tipBody;
    private int _hoverTech = -1;

    public override void _Ready()
    {
        MouseFilter = Control.MouseFilterEnum.Ignore;
        BuildTree();

        _tip = new PanelContainer();
        var tipStyle = new StyleBoxFlat();
        tipStyle.BgColor = new Color(0f, 0f, 0f, 0.88f);
        tipStyle.CornerRadiusTopLeft = 8;
        tipStyle.CornerRadiusTopRight = 8;
        tipStyle.CornerRadiusBottomLeft = 8;
        tipStyle.CornerRadiusBottomRight = 8;
        tipStyle.ContentMarginLeft = 12f;
        tipStyle.ContentMarginRight = 12f;
        tipStyle.ContentMarginTop = 8f;
        tipStyle.ContentMarginBottom = 8f;
        _tip.AddThemeStyleboxOverride("panel", tipStyle);
        _tip.MouseFilter = Control.MouseFilterEnum.Ignore;
        _tip.Visible = false;
        AddChild(_tip);

        _tipBody = new Label();
        _tipBody.AddThemeFontSizeOverride("font_size", 14);
        _tipBody.AddThemeColorOverride("font_color", Colors.White);
        _tipBody.MouseFilter = Control.MouseFilterEnum.Ignore;
        _tip.AddChild(_tipBody);
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree())
        {
            _hoverTech = -1;
            if (_tip != null) _tip.Visible = false;
            return;
        }

        Vector2 mouse = GetViewport().GetMousePosition();
        Vector2 local = GetGlobalTransform().AffineInverse() * mouse;

        int found = -1;
        foreach (var kvp in _centers)
        {
            var rect = new Rect2(kvp.Value - new Vector2(NodeSize / 2f, NodeSize / 2f), new Vector2(NodeSize, NodeSize));
            if (rect.HasPoint(local))
            {
                found = kvp.Key;
                break;
            }
        }

        if (found != _hoverTech)
        {
            _hoverTech = found;
            if (_hoverTech >= 0)
                RefreshTipText();
            if (_tip != null)
                _tip.Visible = _hoverTech >= 0;
        }

        if (_hoverTech >= 0 && _tip != null && _tip.Visible)
        {
            Vector2 vp = GetViewportRect().Size;
            Vector2 want = mouse + new Vector2(18, 24);
            Vector2 size = _tip.GetCombinedMinimumSize();
            Vector2 clamped = new Vector2(
                Mathf.Min(want.X, vp.X - size.X - 8f),
                Mathf.Min(want.Y, vp.Y - size.Y - 8f));
            _tip.Position = GetGlobalTransform().AffineInverse() * clamped;
        }
    }

    private void RefreshTipText()
    {
        if (_tipBody == null || _hoverTech < 0 || _nation < 0) return;
        int tech = _hoverTech;

        string text = $"{GameManager.TechName(tech)}\n{GameManager.TechDesc(tech)}";
        if (GameManager.HasTech(_nation, tech))
        {
            text += "\n✓ Изучено";
        }
        else
        {
            string reqText = "";
            bool reqOk = true;
            foreach (int req in GameManager.TechRequires(tech))
            {
                if (!GameManager.HasTech(_nation, req)) reqOk = false;
                reqText += (reqText.Length > 0 ? ", " : "") + GameManager.TechName(req);
            }
            if (!reqOk)
                text += $"\nТребует: {reqText}";
            else
                text += $"\nЦена: {GameManager.TechGoldCost(tech)}з + {GameManager.TechResearchCost(tech)} ОИ";
        }
        _tipBody.Text = text;
    }

    private void BuildTree()
    {
        // Раскладка: ветка → тир → список тешек. Ширина тира зависит от
        // числа узлов в нём, иначе тиры налезут друг на друга.
        var layout = new Dictionary<int, List<int>>();
        for (int tech = 0; tech < 11; tech++)
        {
            int key = (GameManager.TechIsMilitary(tech) ? 0 : 10) + GameManager.TechDepth(tech);
            if (!layout.ContainsKey(key))
                layout[key] = new List<int>();
            layout[key].Add(tech);
        }

        AddBranchTitle("Война", RowYWar);
        AddBranchTitle("Экономика", RowYEco);

        const float slot = NodeSize + 30f;
        var orderedKeys = new List<int>(layout.Keys);
        orderedKeys.Sort();

        // x-курсор на ветку: тиры идут кумулятивно.
        var branchCursor = new Dictionary<int, float> { { 0, 60f }, { 1, 60f } };
        foreach (int key in orderedKeys)
        {
            int branch = key >= 10 ? 1 : 0;
            var techs = layout[key];
            float rowY = branch == 0 ? RowYWar : RowYEco;
            float colX = branchCursor[branch];

            // Полоса ряда: −1 вверх, +1 вниз, 0 по стволу.
            var lanes = new Dictionary<int, List<int>> { { -1, new List<int>() }, { 0, new List<int>() }, { 1, new List<int>() } };
            foreach (int t in techs)
                lanes[GameManager.TechLane(t)].Add(t);

            foreach (var laneKvp in lanes)
            {
                var laneTechs = laneKvp.Value;
                if (laneTechs.Count == 0) continue;
                for (int i = 0; i < laneTechs.Count; i++)
                {
                    int techId = laneTechs[i];
                    float x = colX + i * slot;
                    float y = rowY + 30f + laneKvp.Key * LaneDy;
                var center = new Vector2(x + NodeSize / 2f, y + NodeSize / 2f);
                _centers[techId] = center;

                var btn = new TextureButton();
                btn.Position = new Vector2(x, y);
                btn.CustomMinimumSize = new Vector2(NodeSize, NodeSize);
                btn.Size = new Vector2(NodeSize, NodeSize);
                btn.Pressed += () => BuyPressed?.Invoke(techId);
                AddChild(btn);
                _nodes[techId] = btn;

                var caption = new Label();
                caption.Position = new Vector2(x - 20f, y + NodeSize + 2f);
                caption.Size = new Vector2(NodeSize + 40f, LabelHeight);
                caption.HorizontalAlignment = HorizontalAlignment.Center;
                caption.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                caption.AddThemeFontSizeOverride("font_size", 12);
                caption.AddThemeColorOverride("font_color", Colors.White);
                caption.MouseFilter = Control.MouseFilterEnum.Ignore;
                AddChild(caption);
                _captions[techId] = caption;
                }
            }

            branchCursor[branch] = colX + techs.Count * slot + ColGap;
        }
    }

    private void AddBranchTitle(string text, float rowY)
    {
        var label = new Label();
        label.Text = text;
        label.Position = new Vector2(60f, rowY);
        label.AddThemeFontSizeOverride("font_size", 16);
        label.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(label);
    }

    // Плейсхолдер квадрата. Когда появятся текстуры — вернуть
    // GD.Load("res://Assets/tech_{id}.png") с фолбэком сюда.
    private Texture2D MakeNodeTexture(int state)
    {
        if (_placeholderCache.TryGetValue(state, out var cached))
            return cached;

        Color color = state switch
        {
            2 => new Color(0.25f, 0.7f, 0.35f),
            1 => new Color(0.2f, 0.5f, 0.85f),
            _ => new Color(0.3f, 0.3f, 0.35f),
        };

        var img = Image.CreateEmpty(NodeSize, NodeSize, false, Image.Format.Rgba8);
        img.Fill(color);
        // Рамка темнее для читаемости.
        var border = color.Darkened(0.35f);
        for (int x = 0; x < NodeSize; x++)
        {
            for (int y = 0; y < 4; y++) img.SetPixel(x, y, border);
            for (int y = NodeSize - 4; y < NodeSize; y++) img.SetPixel(x, y, border);
        }
        for (int y = 0; y < NodeSize; y++)
        {
            for (int x = 0; x < 4; x++) img.SetPixel(x, y, border);
            for (int x = NodeSize - 4; x < NodeSize; x++) img.SetPixel(x, y, border);
        }

        var tex = ImageTexture.CreateFromImage(img);
        _placeholderCache[state] = tex;
        return tex;
    }

    public void Refresh(int nation, int gold, int pts, bool god, bool observer)
    {
        _nation = nation;
        _gold = gold;
        _pts = pts;
        _god = god;
        _observer = observer;

        for (int tech = 0; tech < 11; tech++)
        {
            if (!_nodes.TryGetValue(tech, out var btn)) continue;

            bool owned = GameManager.HasTech(nation, tech);
            bool reqOk = true;
            string reqText = "";
            foreach (int req in GameManager.TechRequires(tech))
            {
                if (!GameManager.HasTech(nation, req)) reqOk = false;
                reqText += (reqText.Length > 0 ? ", " : "") + GameManager.TechName(req);
            }

            int gc = GameManager.TechGoldCost(tech);
            int rc = GameManager.TechResearchCost(tech);
            int state;
            string caption;
            bool enabled;

            if (owned)
            {
                state = 2;
                caption = $"✓ {GameManager.TechName(tech)}";
                enabled = false;
            }
            else if (!reqOk)
            {
                state = 0;
                caption = $"{GameManager.TechName(tech)}\nНужны: {reqText}";
                enabled = false;
            }
            else
            {
                state = 1;
                caption = god
                    ? $"{GameManager.TechName(tech)}\n{GameManager.TechDesc(tech)}"
                    : $"{GameManager.TechName(tech)}\n{gc}з + {rc} ОИ";
                enabled = !observer && (god || (gold >= gc && pts >= rc));
            }

            btn.TextureNormal = MakeNodeTexture(state);
            btn.Disabled = !enabled;
            if (_captions.TryGetValue(tech, out var cap))
                cap.Text = caption;
        }

        if (_hoverTech >= 0)
            RefreshTipText();
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_nation < 0) return;

        foreach (var kvp in _centers)
        {
            int tech = kvp.Key;
            foreach (int req in GameManager.TechRequires(tech))
            {
                if (!_centers.TryGetValue(req, out var from)) continue;
                Vector2 to = kvp.Value;

                Color color;
                if (GameManager.HasTech(_nation, tech))
                    color = new Color(0.3f, 0.9f, 0.4f, 0.9f);
                else if (GameManager.HasTech(_nation, req))
                    color = new Color(1f, 0.85f, 0.3f, 0.9f);
                else
                    color = new Color(0.4f, 0.4f, 0.45f, 0.7f);

                Vector2 dir = to - from;
                if (dir.Length() < 1f) continue;
                dir = dir.Normalized();
                DrawLine(from + dir * 40f, to - dir * 40f, color, 3f);

                // Наконечник у зависимого узла.
                Vector2 tip = to - dir * 40f;
                Vector2 perp = new Vector2(-dir.Y, dir.X);
                DrawColoredPolygon(new Vector2[] { tip, tip - dir * 10f + perp * 5f, tip - dir * 10f - perp * 5f }, color);
            }
        }
    }
}
