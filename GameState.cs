using System;
using System.Collections.Generic;

[Serializable]
public class GameState
{
    public Dictionary<int, NationData> Nations = new();
    public Dictionary<int, RegionData> Regions = new();
    public Dictionary<int, ArmyData> Armies = new();
    public Dictionary<int, EconomyData> Economy = new();
    public int NextArmyId = 0;
}

[Serializable]
public class NationData
{
    public int Id;
    public string Name;
    public float R, G, B;
    public bool IsPlayer;
}

[Serializable]
public class RegionData
{
    public int Id;
    public string RegionName;
    public float CenterX, CenterY;
    public int NationId;
    public int OwnerId;
    public int Gold;
    public int GarrisonPower;
    public List<int> Neighbors = new();
}

[Serializable]
public class ArmyData
{
    public int Id;
    public int RegionId;
    public int PlayerId;
    public int Soldiers;
    public int HP;
    public int MaxHP;
    public bool IsWounded;
    public UnitType Type = UnitType.Infantry;
}

[Serializable]
public class EconomyData
{
    public int Gold;
    public int Mines;
    public float BuildTimer;
}

[Serializable]
public class GameCommand
{
    public CommandType Type;
    public int PlayerId;
    public int Arg1;
    public int Arg2;
    public int Arg3;
}

public enum CommandType
{
    CreateArmy,
    MoveArmy,
    BuildMine,
    BuildFort,
    MergeArmies,
    BuildUniversity,
    Research,
    SetRelation,
    SelectArmy,
    DeselectArmy,
}

public enum BattleResultType
{
    AttackerWon,
    DefenderWon,
    Draw,
}

public enum RelationState
{
    Neutral = 0,
    War = 1,
    Pact = 2,
}

public enum UnitType
{
    Militia = 0,
    Infantry = 1,
    Elite = 2,
}

public static class UnitStats
{
    public static float CostMult(UnitType t)
    {
        return t switch
        {
            UnitType.Militia => 0.6f,
            UnitType.Elite => 2.0f,
            _ => 1.0f,
        };
    }

    public static float DamageMult(UnitType t)
    {
        return t switch
        {
            UnitType.Militia => 0.8f,
            UnitType.Elite => 2.5f,
            _ => 1.0f,
        };
    }

    public static float MoveDelay(UnitType t)
    {
        return t == UnitType.Elite ? 1.2f : 1.5f;
    }

    public static string ShortName(UnitType t)
    {
        return t switch
        {
            UnitType.Militia => "М",
            UnitType.Elite => "Э",
            _ => "П",
        };
    }
}
