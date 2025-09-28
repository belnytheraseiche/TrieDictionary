
using BelNytheraSeiche.TrieDictionary;

[TestClass]
public class BitSetsTests
{
    [TestInitialize]
    public void Setup()
    {
    }

    [TestMethod]
    public void BitSet_AddAndGet_ShouldWorkCorrectly()
    {
        var bitSet = new BitSet();

        bitSet.Add(true);
        bitSet.Add(false);
        bitSet.Add(true);
        bitSet.Add(true);

        Assert.IsTrue(bitSet.Get(0));
        Assert.IsFalse(bitSet.Get(1));
        Assert.IsTrue(bitSet.Get(2));
        Assert.IsTrue(bitSet.Get(3));
    }

    [TestMethod]
    public void ImmutableBitSet_Indexing_ShouldWorkCorrectly()
    {
        var bitSet = new BitSet();
        bitSet.Add(true);
        bitSet.Add(false);
        bitSet.Add(true);
        var immutable = bitSet.ToImmutable();

        Assert.AreEqual(3, immutable.Count);
        Assert.IsTrue(immutable[0]);
        Assert.IsFalse(immutable.At(1));
        Assert.IsTrue(immutable[2]);
    }

    [TestMethod]
    public void RankSelectBitSet_Rank1_ShouldReturnCorrectCount()
    {
        // Corresponds to the bit string: 10110100
        var bitSet = new BitSet();
        bitSet.Add(true);
        bitSet.Add(false);
        bitSet.Add(true);
        bitSet.Add(true);
        bitSet.Add(false);
        bitSet.Add(true);
        bitSet.Add(false);
        bitSet.Add(false);

        var rankSelect = bitSet.ToRankSelect(true);

        Assert.AreEqual(0, rankSelect.Rank1(-1)); // Edge case: negative index
        Assert.AreEqual(1, rankSelect.Rank1(0));  // "1"
        Assert.AreEqual(1, rankSelect.Rank1(1));  // "10"
        Assert.AreEqual(2, rankSelect.Rank1(2));  // "101"
        Assert.AreEqual(3, rankSelect.Rank1(3));  // "1011"
        Assert.AreEqual(4, rankSelect.Rank1(5));  // "101101"
        Assert.AreEqual(4, rankSelect.Rank1(7));  // "10110100"
    }

    [TestMethod]
    public void RankSelectBitSet_Select1_ShouldFindNthBit()
    {
        // Corresponds to the bit string: 10110100
        var bitSet = new BitSet();
        bitSet.Add(true);
        bitSet.Add(false);
        bitSet.Add(true);
        bitSet.Add(true);
        bitSet.Add(false);
        bitSet.Add(true);
        var rankSelect = bitSet.ToRankSelect(true);

        Assert.AreEqual(-1, rankSelect.Select1(0)); // Edge case: k=0
        Assert.AreEqual(0, rankSelect.Select1(1));  // 1st '1' is at index 0
        Assert.AreEqual(2, rankSelect.Select1(2));  // 2nd '1' is at index 2
        Assert.AreEqual(3, rankSelect.Select1(3));  // 3rd '1' is at index 3
        Assert.AreEqual(5, rankSelect.Select1(4));  // 4th '1' is at index 5
        Assert.AreEqual(-1, rankSelect.Select1(5)); // Edge case: 5th '1' does not exist
    }

    [TestMethod]
    public void RankSelectBitSet_Select0_ShouldFindNthBit()
    {
        // Corresponds to the bit string: 10110100
        var bitSet = new BitSet();
        bitSet.Add(true);
        bitSet.Add(false);
        bitSet.Add(true);
        bitSet.Add(true);
        bitSet.Add(false);
        bitSet.Add(true);
        bitSet.Add(false);
        bitSet.Add(false);
        var rankSelect = bitSet.ToRankSelect(true);

        Assert.AreEqual(1, rankSelect.Select0(1));  // 1st '0' is at index 1
        Assert.AreEqual(4, rankSelect.Select0(2));  // 2nd '0' is at index 4
        Assert.AreEqual(6, rankSelect.Select0(3));  // 3rd '0' is at index 6
        Assert.AreEqual(7, rankSelect.Select0(4));  // 4th '0' is at index 7
    }
}
