using LuckyDogRise.Tools;

var cases = 0;
foreach (var (columns, rows) in new[] { (4, 3), (12, 8) })
for (var seed = 0; seed < 32; seed++)
{
    var width = columns + 2;
    var height = rows + 2;
    var dogPool = Enumerable.Range(1002, 36).ToArray();
    var hatPool = Enumerable.Range(1, 34).ToArray();
    var backgroundPool = Enumerable.Range(1, 19).ToArray();
    var dogs = SteamStoreCaptureLayout.Assign(width, height, dogPool, new Random(seed), 1001);
    var hats = SteamStoreCaptureLayout.Assign(width, height, hatPool, new Random(seed + 1000));
    var backgrounds = SteamStoreCaptureLayout.Assign(width, height, backgroundPool, new Random(seed + 2000));
    Check(dogs.Count(id => id == 1001) == 1, "1001 must occur once");
    Check(SteamStoreCaptureLayout.NeighborsAreDistinct(dogs, width, height), "adjacent dog appearances differ");
    Check(SteamStoreCaptureLayout.NeighborsAreDistinct(hats, width, height), "adjacent hats differ");
    Check(SteamStoreCaptureLayout.NeighborsAreDistinct(backgrounds, width, height), "adjacent backgrounds differ");

    // Moving in every direction merely permutes the toroidal grid, including its seams.
    for (var dy = -1; dy <= 1; dy++)
    for (var dx = -1; dx <= 1; dx++)
    {
        var movedDogs = Shift(dogs, columns, rows, dx, dy);
        var movedHats = Shift(hats, columns, rows, dx, dy);
        var movedBackgrounds = Shift(backgrounds, columns, rows, dx, dy);
        Check(movedDogs.Count(id => id == 1001) == 1, "scroll preserves the unique 1001");
        Check(SteamStoreCaptureLayout.NeighborsAreDistinct(movedDogs, width, height), "scroll seam preserves dog differences");
        Check(SteamStoreCaptureLayout.NeighborsAreDistinct(movedHats, width, height), "scroll seam preserves hat differences");
        Check(SteamStoreCaptureLayout.NeighborsAreDistinct(movedBackgrounds, width, height), "scroll seam preserves background differences");
    }
    cases++;
}

try
{
    SteamStoreCaptureLayout.Assign(6, 5, Enumerable.Range(1, 8).ToArray(), new Random(1));
    throw new Exception("eight choices should be rejected");
}
catch (ArgumentException) { }

Console.WriteLine($"SteamStoreCaptureLayout: {cases} layouts and all 9 scroll directions passed.");

static int[] Shift(int[] values, int columns, int rows, int dx, int dy)
{
    var width = columns + 2;
    var height = rows + 2;
    var result = new int[values.Length];
    for (var row = -1; row <= rows; row++)
    for (var column = -1; column <= columns; column++)
    {
        var source = (row + 1) * width + column + 1;
        var nextColumn = SteamStoreCaptureLayout.WrapCoordinate(column, columns, dx);
        var nextRow = SteamStoreCaptureLayout.WrapCoordinate(row, rows, dy);
        var destination = (nextRow + 1) * width + nextColumn + 1;
        result[destination] = values[source];
    }
    return result;
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
