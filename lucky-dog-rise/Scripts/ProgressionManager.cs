using Godot;
using System;

namespace LuckyDogRise;

public enum PlayerRank
{
    HomeGame,    // 0 - bare arm
    CasinoReg,   // 1000 - dress shirt
    Tournament,  // 10000 - baseball jacket
    HighStakes,  // 100000 - suit
    Champion     // 1000000 - WSOP bracelet
}

public class ProgressionManager
{
    private const string SavePath = "user://progress.cfg";

    public int HighScore { get; private set; }
    public PlayerRank CurrentRank { get; private set; }
    private PlayerRank _rankBeforeHand;

    private static readonly (PlayerRank rank, int threshold)[] Ranks = new[]
    {
        (PlayerRank.HomeGame, 0),
        (PlayerRank.CasinoReg, 1000),
        (PlayerRank.Tournament, 10000),
        (PlayerRank.HighStakes, 100000),
        (PlayerRank.Champion, 1000000),
    };

    public ProgressionManager()
    {
        Load();
    }

    public void UpdateHighScore(int currentChips)
    {
        _rankBeforeHand = CurrentRank;
        if (currentChips > HighScore)
        {
            HighScore = currentChips;
            UpdateRank();
            Save();
        }
    }

    public bool CheckRankUp()
    {
        return CurrentRank > _rankBeforeHand;
    }

    public void Reset()
    {
        // Don't reset high score - it persists
        _rankBeforeHand = CurrentRank;
    }

    private void UpdateRank()
    {
        for (int i = Ranks.Length - 1; i >= 0; i--)
        {
            if (HighScore >= Ranks[i].threshold)
            {
                CurrentRank = Ranks[i].rank;
                return;
            }
        }
    }

    private void Save()
    {
        var config = new ConfigFile();
        config.SetValue("progress", "high_score", HighScore);
        config.Save(SavePath);
    }

    private void Load()
    {
        var config = new ConfigFile();
        if (config.Load(SavePath) == Error.Ok)
        {
            HighScore = (int)(long)config.GetValue("progress", "high_score", 0);
        }
        UpdateRank();
        _rankBeforeHand = CurrentRank;
    }
}
