using Godot;
using System.Collections.Generic;

// Центр иконок валют: gold / science.
// Грузит res://Assets/<id>.png через пайплайн импорта (работает в экспорте);
// файла нет — генерированный плейсхолдер, ничего не падает.
public static class GameIcons
{
    private static readonly Dictionary<string, Texture2D> _cache = new();

    public static Texture2D Get(string id)
    {
        if (_cache.TryGetValue(id, out var tex) && tex != null)
            return tex;

        Texture2D loaded = null;
        try { loaded = GD.Load<Texture2D>($"res://Assets/{id}.png"); } catch { }
        tex = loaded ?? MakePlaceholder(id);
        _cache[id] = tex;
        GD.Print($"[Icons] {id}: {(loaded != null ? $"файл {tex.GetWidth()}×{tex.GetHeight()}" : "плейсхолдер")}");
        return tex;
    }

    public static TextureRect MakeIcon(string id, float size = 20f)
    {
        var rect = new TextureRect();
        rect.Texture = Get(id);
        rect.CustomMinimumSize = new Vector2(size, size);
        rect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        rect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        rect.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return rect;
    }

    private static Texture2D MakePlaceholder(string id)
    {
        const int size = 32;
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        Vector2 c = new Vector2(size / 2f, size / 2f);

        if (id == "science")
        {
            // Синяя колба: круг + горлышко.
            FillCircle(img, c + new Vector2(0, 5), 9f, new Color(0.3f, 0.6f, 1f));
            for (int y = 2; y < 12; y++)
                for (int x = 13; x < 19; x++)
                    img.SetPixel(x, y, new Color(0.3f, 0.6f, 1f));
        }
        else if (id == "militia")
        {
            // Серый щит.
            FillCircle(img, c, 11f, new Color(0.55f, 0.55f, 0.6f));
            FillCircle(img, c, 7f, new Color(0.4f, 0.4f, 0.45f));
            for (int y = 6; y < 26; y++)
                for (int x = 15; x < 17; x++)
                    img.SetPixel(x, y, new Color(0.7f, 0.7f, 0.75f));
        }
        else if (id == "elite")
        {
            // Золотой меч: клинок + гарда.
            for (int y = 3; y < 22; y++)
                for (int x = 15; x < 17; x++)
                    img.SetPixel(x, y, new Color(1f, 0.85f, 0.3f));
            for (int y = 22; y < 24; y++)
                for (int x = 10; x < 22; x++)
                    img.SetPixel(x, y, new Color(0.9f, 0.7f, 0.2f));
            for (int y = 24; y < 29; y++)
                for (int x = 15; x < 17; x++)
                    img.SetPixel(x, y, new Color(0.5f, 0.35f, 0.15f));
        }
        else
        {
            // Золотая монета.
            FillCircle(img, c, 11f, new Color(1f, 0.8f, 0.2f));
            FillCircle(img, c, 7f, new Color(0.85f, 0.6f, 0.1f));
        }

        return ImageTexture.CreateFromImage(img);
    }

    private static void FillCircle(Image img, Vector2 center, float radius, Color color)
    {
        int r = (int)Mathf.Ceil(radius);
        for (int y = (int)(center.Y - r); y <= center.Y + r; y++)
        {
            for (int x = (int)(center.X - r); x <= center.X + r; x++)
            {
                if (x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight()) continue;
                if (new Vector2(x, y).DistanceTo(center) <= radius)
                    img.SetPixel(x, y, color);
            }
        }
    }
}
