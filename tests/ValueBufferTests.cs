
using BelNytheraSeiche.TrieDictionary;

[TestClass]
public class ValueBufferTests
{
    ValueBuffer<int> buffer_ = new(8, false);

    [TestInitialize]
    public void Setup()
    {
    }

    [TestMethod]
    public void Constructor_ShouldWord()
    {
        var result = new bool[10];
        for (var i = 0; i < result.Length; i++)
        {
            bool success = false;
            try
            {
                var _ = new ValueBuffer<int>(i);
                success = true;
            }
            catch
            {
            }
            result[i] = success;
        }

        CollectionAssert.AreEqual(result, (bool[])[false, true, true, false, true, false, false, false, true, false]);
    }

    [TestMethod]
    public void Indexer_ShouldWork()
    {
        buffer_.ExtendCapacity(buffer_.ElementsPerChunk);
        for (var i = 0; i < 8; i++)
            buffer_[i] = i;
        var result1 = new int[buffer_.Capacity];
        for (var i = 0; i < buffer_.Count; i++)
            result1[i] = buffer_[i];
        var result2 = false;
        try
        {
            buffer_[8] = 8;
        }
        catch
        {
            result2 = true;
        }
        CollectionAssert.AreEqual(result1, (int[])[0, 1, 2, 3, 4, 5, 6, 7]);
        Assert.IsTrue(result2);
    }

    [TestMethod]
    public void ExtendCapacity_ShouldWork()
    {
        var result = new List<(int, int, int, int)>();
        foreach (var n in (int[])[0, 4, 8])
        {
            var buffer = new ValueBuffer<int>((int)Math.Pow(2.0, n));
            var capacity1 = buffer.Capacity;
            var capacity2 = buffer.ExtendCapacity((int)Math.Pow(2.0, n) - 1);
            buffer.Clear();
            var capacity3 = buffer.ExtendCapacity((int)Math.Pow(2.0, n));
            buffer.Clear();
            var capacity4 = buffer.ExtendCapacity((int)Math.Pow(2.0, n) + 1);
            result.Add((capacity1, capacity2, capacity3, capacity4));
        }
        CollectionAssert.AreEqual(result, ((int, int, int, int)[])[(0, 1, 1, 2), (0, 16, 16, 32), (0, 256, 256, 512)]);
    }

    [TestMethod]
    public void Fill_ShouldWork()
    {
        buffer_.ExtendCapacity(8);
        buffer_.Fill(3, 4, 2);
        buffer_[7] = 0;
        var result = buffer_.ToArray();
        CollectionAssert.AreEqual(result, (int[])[0, 0, 0, 0, 3, 3, 0, 0]);
    }

    [TestMethod]
    public void ExtendAutomatically_ShouldWork()
    {
        var buffer = new ValueBuffer<int>(4, true);
        for (var i = 0; i < 13; i++)
            buffer[i] = i;
        var result = buffer.ToArray();
        CollectionAssert.AreEqual(result, (int[])[0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]);
    }

    [TestMethod]
    public void ToArray_ShouldWork()
    {
        var buffer = new ValueBuffer<int>(2, true);
        var result1 = buffer.ToArray().Length;

        buffer.Add(1);
        var result2 = buffer.ToArray();

        buffer.Add(2);
        buffer.Add(3);
        var result3 = buffer.ToArray();

        buffer.Add(4);
        var result4 = buffer.ToArray();

        Assert.AreEqual(result1, 0);
        CollectionAssert.AreEqual(result2, (int[])[1]);
        CollectionAssert.AreEqual(result3, (int[])[1, 2, 3]);
        CollectionAssert.AreEqual(result4, (int[])[1, 2, 3, 4]);
    }
}
