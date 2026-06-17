using Godot;
using Luban.SimpleJSON;

namespace LuckyDogRise;

public static class LubanData
{
    private static DataTables.Tables _tables = null!;

    public static DataTables.Tables Tables
    {
        get
        {
            _tables ??= new DataTables.Tables(name =>
            {
                var path = $"res://Data/Json/{name}.json";
                using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
                if (file == null)
                    throw new System.IO.FileNotFoundException($"Luban data file not found: {path}");
                return JSONNode.Parse(file.GetAsText());
            });
            return _tables;
        }
    }
}
