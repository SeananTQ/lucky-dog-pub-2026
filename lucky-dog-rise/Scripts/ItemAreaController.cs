using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LuckyDogRise;

public partial class ItemAreaController : Node2D
{
    private readonly List<Sprite2D> _items = new();

    public override void _Ready()
    {
        foreach (var child in GetChildren())
        {
            if (child is Sprite2D sprite)
                _items.Add(sprite);
        }
        ShowRandom();
    }

    public void ShowRandom()
    {
        foreach (var item in _items)
            item.Visible = false;

        if (_items.Count > 0)
            _items[new Random().Next(_items.Count)].Visible = true;
    }

    public void ShowByName(string name)
    {
        foreach (var item in _items)
            item.Visible = item.Name == name;
    }
}
