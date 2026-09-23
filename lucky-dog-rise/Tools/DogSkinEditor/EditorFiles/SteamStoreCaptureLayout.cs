using System;
using System.Collections.Generic;
using System.Linq;

namespace LuckyDogRise.Tools;

/// <summary>Pure layout rules shared by the recording stage and its unit tests.</summary>
public static class SteamStoreCaptureLayout
{
    public static int[] Assign(int width, int height, IReadOnlyList<int> pool, Random random, int? uniqueId = null)
    {
        if (width < 3 || height < 3) throw new ArgumentOutOfRangeException(nameof(width));
        if (pool.Distinct().Count() < 9)
            throw new ArgumentException("至少需要 9 种不同造型或帽子。", nameof(pool));
        if (uniqueId.HasValue && pool.Contains(uniqueId.Value))
            throw new ArgumentException("唯一造型不能同时出现在普通素材池中。", nameof(uniqueId));

        var values = new int[width * height];
        if (uniqueId.HasValue)
            values[random.Next(values.Length)] = uniqueId.Value;
        var positions = Enumerable.Range(0, values.Length).Where(index => values[index] == 0).ToArray();
        for (var i = positions.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (positions[i], positions[j]) = (positions[j], positions[i]);
        }

        var usage = pool.ToDictionary(value => value, _ => 0);
        foreach (var index in positions)
        {
            var forbidden = NeighborIndices(index, width, height)
                .Select(neighbor => values[neighbor]).Where(value => value != 0).ToHashSet();
            var candidates = pool.Where(value => !forbidden.Contains(value)).ToArray();
            if (candidates.Length == 0)
                throw new InvalidOperationException("当前行列数无法排出八邻域各不相同的矩阵。");
            var leastUsed = candidates.Min(value => usage[value]);
            var choices = candidates.Where(value => usage[value] == leastUsed).ToArray();
            var chosen = choices[random.Next(choices.Length)];
            values[index] = chosen;
            usage[chosen]++;
        }
        return values;
    }

    public static bool NeighborsAreDistinct(IReadOnlyList<int> values, int width, int height)
    {
        if (values.Count != width * height) return false;
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] == 0 || NeighborIndices(index, width, height)
                    .Any(neighbor => values[neighbor] == values[index]))
                return false;
        }
        return true;
    }

    public static int WrapCoordinate(int coordinate, int visibleCount, int delta)
    {
        var span = visibleCount + 2;
        return ((coordinate + 1 + delta) % span + span) % span - 1;
    }

    private static IEnumerable<int> NeighborIndices(int index, int width, int height)
    {
        var column = index % width;
        var row = index / width;
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            yield return ((row + dy + height) % height) * width + (column + dx + width) % width;
        }
    }
}
