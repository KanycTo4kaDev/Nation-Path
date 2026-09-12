using Godot;

public class Nation
{
    public int Id;
    public string Name;
    public Color Color;
    public bool IsPlayer;

    public Nation(int id, string name, Color color)
    {
        Id = id;
        Name = name;
        Color = color;
        IsPlayer = false;
    }
}
