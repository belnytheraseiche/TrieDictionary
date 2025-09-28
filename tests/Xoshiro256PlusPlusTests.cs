
using BelNytheraSeiche.TrieDictionary;

[TestClass]
public class Xoshiro256PlusPlusTests
{
    Xoroshiro128PlusPlus random_ = new();

    [TestInitialize]
    public void Setup()
    {
    }

    [TestMethod]
    public void Next_ShouldWork()
    {
        var result1 = Enumerable.Range(0, 100).Select(n => random_.Next()).All(n => n is >= 0);
        var result2 = Enumerable.Range(0, 100).Select(n => random_.Next(-100, 100)).Any(n => n is >= -100 and < 100);
        Assert.IsTrue(result1);
        Assert.IsTrue(result2);
    }

    [TestMethod]
    public void NextSingle_ShouldWork()
    {
        var result = Enumerable.Range(0, 100).Select(n => random_.NextSingle()).All(n => n is >= 0 and < 1.0f);
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void NextDouble_ShouldWork()
    {
        var result = Enumerable.Range(0, 100).Select(n => random_.NextDouble()).All(n => n is >= 0 and < 1.0);
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void NextBytes_ShouldWork()
    {
        var result = Enumerable.Range(0, 100).Select(n =>
        {
            var bytes = new byte[17];
            random_.NextBytes(bytes);
            return bytes;
        }).All(n => n is not [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        Assert.IsTrue(result);
    }
}
