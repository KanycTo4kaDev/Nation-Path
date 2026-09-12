using Godot;
using System.Collections.Generic;

public partial class Region : Polygon2D
{
    public int Id;
    public string RegionName = "";
    public Vector2 Center;
    public int NationId = -1;
    public int OwnerId = -1;
    public Nation OwningNation = null;
    public int Gold;
    public int GarrisonPower;
    public int FortLevel = 0;
    public List<int> Neighbors = new();

    public void Initialize(int id, string name, Vector2[] vertices, Nation nation)
    {
        Id = id;
        RegionName = name;
        Polygon = vertices;
        Gold = (int)(GD.Randi() % 91 + 10);
        GarrisonPower = (int)(GD.Randi() % 11 + 5);

        Vector2 sum = Vector2.Zero;
        foreach (var v in vertices)
            sum += v;
        Center = vertices.Length > 0 ? sum / vertices.Length : Vector2.Zero;

        if (nation != null)
        {
            NationId = nation.Id;
            OwnerId = nation.Id;
            OwningNation = nation;
            Color = nation.Color;
        }
        else
        {
            NationId = -1;
            OwnerId = -1;
            OwningNation = null;
            Color = Colors.White;
        }
    }
}
