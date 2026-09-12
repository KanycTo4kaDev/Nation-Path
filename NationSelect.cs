using Godot;

public partial class NationSelect : Control
{
    private (int id, string name, Color color)[] _nations = new[]
    {
        (0, "Красный",  new Color(0.9f, 0.25f, 0.25f)),
        (1, "Синий",    new Color(0.25f, 0.4f, 0.9f)),
        (2, "Зелёный",  new Color(0.25f, 0.85f, 0.4f)),
        (3, "Розовый",  new Color(0.9f, 0.45f, 0.7f)),
    };

    private void BuildUI()
    {
        var center = GetViewportRect().Size / 2f;

        var vbox = new VBoxContainer();
        vbox.Position = center - new Vector2(150, 200);
        vbox.CustomMinimumSize = new Vector2(300, 0);
        AddChild(vbox);

        var title = new Label();
        title.Text = "Выберите государство";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 24);
        vbox.AddChild(title);

        var spacer0 = new Control();
        spacer0.CustomMinimumSize = new Vector2(0, 15);
        vbox.AddChild(spacer0);

        var multiBtn = new Button();
        multiBtn.Text = "Сетевая игра";
        multiBtn.CustomMinimumSize = new Vector2(300, 40);
        multiBtn.AddThemeFontSizeOverride("font_size", 16);
        if (GameManager.Instance.IsMultiplayerGame)
        {
            multiBtn.Visible = false;
        }
        var multiStyle = new StyleBoxFlat();
        multiStyle.BgColor = new Color(0.2f, 0.5f, 0.8f);
        multiStyle.CornerRadiusTopLeft = 8;
        multiStyle.CornerRadiusTopRight = 8;
        multiStyle.CornerRadiusBottomLeft = 8;
        multiStyle.CornerRadiusBottomRight = 8;
        multiBtn.AddThemeStyleboxOverride("normal", multiStyle);
        var multiHover = new StyleBoxFlat();
        multiHover.BgColor = new Color(0.25f, 0.55f, 0.9f);
        multiHover.CornerRadiusTopLeft = 8;
        multiHover.CornerRadiusTopRight = 8;
        multiHover.CornerRadiusBottomLeft = 8;
        multiHover.CornerRadiusBottomRight = 8;
        multiBtn.AddThemeStyleboxOverride("hover", multiHover);
        multiBtn.AddThemeColorOverride("font_color", Colors.White);
        multiBtn.AddThemeColorOverride("font_hover_color", Colors.White);
        multiBtn.Pressed += () => GetTree().ChangeSceneToFile("res://LobbyScreen.tscn");
        vbox.AddChild(multiBtn);

        var godBtn = new Button();
        godBtn.Text = "▶ Режим бога";
        godBtn.CustomMinimumSize = new Vector2(300, 40);
        godBtn.AddThemeFontSizeOverride("font_size", 16);
        if (GameManager.Instance.IsMultiplayerGame)
        {
            godBtn.Visible = false;
        }
        var godStyle = new StyleBoxFlat();
        godStyle.BgColor = new Color(0.5f, 0.35f, 0.1f);
        godStyle.CornerRadiusTopLeft = 8;
        godStyle.CornerRadiusTopRight = 8;
        godStyle.CornerRadiusBottomLeft = 8;
        godStyle.CornerRadiusBottomRight = 8;
        godBtn.AddThemeStyleboxOverride("normal", godStyle);
        var godHover = new StyleBoxFlat();
        godHover.BgColor = new Color(0.6f, 0.42f, 0.12f);
        godHover.CornerRadiusTopLeft = 8;
        godHover.CornerRadiusTopRight = 8;
        godHover.CornerRadiusBottomLeft = 8;
        godHover.CornerRadiusBottomRight = 8;
        godBtn.AddThemeStyleboxOverride("hover", godHover);
        godBtn.AddThemeColorOverride("font_color", Colors.White);
        godBtn.AddThemeColorOverride("font_hover_color", Colors.White);
        godBtn.Pressed += () =>
        {
            AudioHub.Instance?.PlayClick();
            GameManager.Instance.IsGodMode = true;
            GameManager.Instance.IsMultiplayerGame = false;
            GameManager.Instance.PlayerNationId = 0;
            GetTree().ChangeSceneToFile("res://Main.tscn");
        };
        vbox.AddChild(godBtn);

        if (!GameManager.Instance.IsMultiplayerGame)
        {
            var diffRow = new HBoxContainer();
            diffRow.Alignment = BoxContainer.AlignmentMode.Center;
            diffRow.AddThemeConstantOverride("separation", 8);
            vbox.AddChild(diffRow);

            var diffLabel = new Label();
            diffLabel.Text = "ИИ:";
            diffLabel.AddThemeFontSizeOverride("font_size", 14);
            diffLabel.AddThemeColorOverride("font_color", Colors.LightGray);
            diffRow.AddChild(diffLabel);

            var diffOption = new OptionButton();
            diffOption.AddItem("Легко", 0);
            diffOption.AddItem("Норма", 1);
            diffOption.Selected = GameManager.Instance.AIDifficulty;
            diffOption.AddThemeFontSizeOverride("font_size", 14);
            diffOption.ItemSelected += (long idx) =>
            {
                GameManager.Instance.AIDifficulty = (int)idx;
                AudioHub.Instance?.PlayClick();
            };
            diffRow.AddChild(diffOption);
        }

        var spacerSep = new Control();
        spacerSep.CustomMinimumSize = new Vector2(0, 10);
        vbox.AddChild(spacerSep);

        var sep = new HSeparator();
        if (GameManager.Instance.IsMultiplayerGame)
        {
            sep.Visible = false;
        }
        vbox.AddChild(sep);

        var spacer1 = new Control();
        spacer1.CustomMinimumSize = new Vector2(0, 15);
        vbox.AddChild(spacer1);

        foreach (var nation in _nations)
        {
            var btn = new Button();
            btn.Text = nation.name;
            btn.CustomMinimumSize = new Vector2(300, 60);
            btn.AddThemeFontSizeOverride("font_size", 20);

            var style = new StyleBoxFlat();
            style.BgColor = nation.color;
            style.CornerRadiusTopLeft = 8;
            style.CornerRadiusTopRight = 8;
            style.CornerRadiusBottomLeft = 8;
            style.CornerRadiusBottomRight = 8;
            style.ContentMarginTop = 10;
            style.ContentMarginBottom = 10;
            btn.AddThemeStyleboxOverride("normal", style);

            var hoverStyle = new StyleBoxFlat();
            hoverStyle.BgColor = nation.color.Lightened(0.15f);
            hoverStyle.CornerRadiusTopLeft = 8;
            hoverStyle.CornerRadiusTopRight = 8;
            hoverStyle.CornerRadiusBottomLeft = 8;
            hoverStyle.CornerRadiusBottomRight = 8;
            hoverStyle.ContentMarginTop = 10;
            hoverStyle.ContentMarginBottom = 10;
            btn.AddThemeStyleboxOverride("hover", hoverStyle);

            var pressedStyle = new StyleBoxFlat();
            pressedStyle.BgColor = nation.color.Darkened(0.1f);
            pressedStyle.CornerRadiusTopLeft = 8;
            pressedStyle.CornerRadiusTopRight = 8;
            pressedStyle.CornerRadiusBottomLeft = 8;
            pressedStyle.CornerRadiusBottomRight = 8;
            pressedStyle.ContentMarginTop = 10;
            pressedStyle.ContentMarginBottom = 10;
            btn.AddThemeStyleboxOverride("pressed", pressedStyle);

            btn.AddThemeColorOverride("font_color", Colors.White);
            btn.AddThemeColorOverride("font_hover_color", Colors.White);
            btn.AddThemeColorOverride("font_pressed_color", Colors.White);

            int nationId = nation.id;
            btn.Pressed += () => OnNationSelected(nationId);

            vbox.AddChild(btn);

            var spacer2 = new Control();
            spacer2.CustomMinimumSize = new Vector2(0, 10);
            vbox.AddChild(spacer2);
        }
    }

    private void OnNationSelected(int nationId)
    {
        AudioHub.Instance?.PlayClick();
        GameManager.Instance.PlayerNationId = nationId;
        GameManager.Instance.IsGodMode = false;
        GetTree().ChangeSceneToFile("res://Main.tscn");
    }

    public override void _Ready()
    {
        if (GameManager.Instance.IsMultiplayerGame)
        {
            GameManager.Instance.IsGodMode = false;
            int localPeerId = NetworkManager.Instance.LocalPeerId;
            int nationId = NetworkManager.Instance.GetNationForPeer(localPeerId);
            if (nationId < 0)
                nationId = Mathf.Clamp(localPeerId - 1, 0, _nations.Length - 1);
            GameManager.Instance.PlayerNationId = nationId;
            GetTree().ChangeSceneToFile("res://Main.tscn");
            return;
        }

        BuildUI();
    }
}
