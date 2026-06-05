using System;

namespace LuckyDogRise;

public static class DogProverbs
{
    private static readonly string[] Proverbs = new[]
    {
        "Life is like a box of chocolates, you never know what you're gonna get... but I'm a dog, and chocolate would kill me.",
        "They say every kid doesn't cry forever, every gambler dog doesn't lose forever? Hey, I used to be human in my past life, believe me?",
        "They say money can't buy happiness... but it can buy treats, and that's basically the same thing.",
        "A wise man once said 'know when to fold them.' I just chewed the cards. Problem solved.",
        "They say the house always wins. But this is MY house. I sleep on that couch.",
        "Life is a gamble... but I just roll over and hope for belly rubs.",
        "They say fortune favors the bold. I chased a car once. Didn't end well.",
        "You miss 100% of the shots you don't take. I miss 100% of the squirrels I chase.",
        "They say every dog has its day. Today was not that day.",
        "When life gives you lemons, make lemonade. When life gives you no chips... take a nap.",
    };

    private static readonly Random Rng = new();

    public static string GetRandom()
    {
        return Proverbs[Rng.Next(Proverbs.Length)];
    }
}
