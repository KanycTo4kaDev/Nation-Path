using Godot;

// Слой индикаторов поверх карты: подсветка пути и стрелки перемещения.
// Рисуется выше регионов и армий (ZIndex), код отрисовки живёт в MapGenerator.
public partial class MoveOverlay : Node2D
{
    private MapGenerator _map;

    public void Setup(MapGenerator map)
    {
        _map = map;
        ZIndex = 100;
    }

    public void Refresh()
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
        _map?.DrawIndicators(this);
    }
}
