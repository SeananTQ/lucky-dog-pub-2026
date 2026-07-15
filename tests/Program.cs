Console.WriteLine(8);
Console.WriteLine();

const int size = 5;

Console.WriteLine("正三角矩阵：");
PrintUprightTriangle(size);

Console.WriteLine("倒三角矩阵：");
PrintInvertedTriangle(size);

Console.WriteLine("等腰三角矩阵：");
PrintIsoscelesTriangle(size);

static void PrintUprightTriangle(int size)
{
    var printedCount = 0;

    for (var row = 1; row <= size; row++)
    {
        Console.WriteLine(CreateNumberRow(row, ref printedCount));
    }
}

static void PrintInvertedTriangle(int size)
{
    var printedCount = 0;

    for (var row = size; row >= 1; row--)
    {
        Console.WriteLine(CreateNumberRow(row, ref printedCount));
    }
}

static void PrintIsoscelesTriangle(int size)
{
    var printedCount = 0;

    for (var row = 1; row <= size; row++)
    {
        var leadingSpaces = size - row;
        var eights = row * 2 - 1;
        Console.WriteLine(new string(' ', leadingSpaces) + CreateNumberRow(eights, ref printedCount));
    }
}

static string CreateNumberRow(int length, ref int printedCount)
{
    var row = new char[length];

    for (var index = 0; index < length; index++)
    {
        printedCount++;
        row[index] = printedCount % 4 == 0 ? '4' : '8';
    }

    return new string(row);
}
