using BelNytheraSeiche.TrieDictionary;

[TestClass]
public class AATreeRecordStoreTests
{
    AATreeRecordStore store_ = new();

    [TestInitialize]
    public void Setup()
    {
    }

    [TestMethod]
    public void Add_RecordAccess_ShouldWork()
    {
        var values1 = new List<(int, int)>();
        for (var i = 0; i < 10000; i++)
        {
            var identifier = Random.Shared.Next(100);
            values1.Add((identifier, i));
            var access = store_.GetRecordAccess(identifier);
            access.Add(BitConverter.GetBytes(i));
        }
        var values2 = new List<(int, int)>();
        foreach (var (identifier, access) in store_.Enumerate())
        {
            foreach (var r in access)
            {
                var value = BitConverter.ToInt32(r.Content);
                values2.Add((identifier, value));
            }
        }
        CollectionAssert.AreEquivalent(values1, values2);
    }

    [TestMethod]
    public void Remove_ShouldWork()
    {
        var values1 = new HashSet<int>();
        for (var i = 0; i < 10000; i++)
        {
            var identifier = Random.Shared.Next(100);
            values1.Add(identifier);
            store_.Add(identifier);
        }
        var values2 = new HashSet<int>();
        for (var i = 0; i < 20; i++)
        {
            var identifier = Random.Shared.Next(100);
            values2.Add(identifier);
            store_.Remove(identifier);
        }
        var values3 = values1.Where(n => !values2.Contains(n)).ToArray();
        var values4 = store_.Enumerate().Select(n => n.Item1).ToArray();
        CollectionAssert.AreEquivalent(values3, values4);
    }

    [TestMethod]
    public void Contains_ShouldWork()
    {
        var values1 = new HashSet<int>();
        for (var i = 0; i < 10000; i++)
        {
            var identifier = Random.Shared.Next(100);
            values1.Add(identifier);
            store_.Add(identifier);
        }
        var values2 = new HashSet<int>();
        for (var i = 0; i < 20; i++)
        {
            var identifier = Random.Shared.Next(100);
            values2.Add(identifier);
            store_.Remove(identifier);
        }
        var values3 = values1.Where(n => !values2.Contains(n)).ToArray();
        var result1 = values3.All(n => store_.Contains(n));
        var result2 = values2.All(n => !store_.Contains(n));
        Assert.IsTrue(result1);
        Assert.IsTrue(result2);
    }

    [TestMethod]
    public void Serialize_Deserialize_ShouldWork()
    {
        var store1 = new AATreeRecordStore();
        var values1 = new List<(int, int)>();
        for (var i = 0; i < 10000; i++)
        {
            var identifier = Random.Shared.Next(1000);
            var access = store1.GetRecordAccess(identifier);
            access.Add(BitConverter.GetBytes(i));
            values1.Add((identifier, i));
        }
        var serialized = BasicRecordStore.Serialize(store1);
        var store2 = BasicRecordStore.Deserialize<AATreeRecordStore>(serialized);
        var values2 = new List<(int, int)>();
        foreach (var (identifier, access) in store2.Enumerate())
        {
            foreach (var r in access)
            {
                values2.Add((identifier, BitConverter.ToInt32(r.Content)));
            }
        }
        var result = values1.Order().SequenceEqual(values2.Order());
        CollectionAssert.AreEquivalent(values1, values2);
    }

    [TestMethod]
    public void Serialize_With_Options_ShouldWork()
    {
        var store1 = new AATreeRecordStore();
        var values1 = new List<(int, int)>();
        for (var i = 0; i < 10000; i++)
        {
            var identifier = Random.Shared.Next(1000);
            var access = store1.GetRecordAccess(identifier);
            access.Add(BitConverter.GetBytes(i));
            values1.Add((identifier, i));
        }
        var options = new BasicRecordStore.SerializationOptions() { CompressionLevel = System.IO.Compression.CompressionLevel.NoCompression };
        var serialized = BasicRecordStore.Serialize(store1, options);
        var store2 = BasicRecordStore.Deserialize<AATreeRecordStore>(serialized);
        var values2 = new List<(int, int)>();
        foreach (var (identifier, access) in store2.Enumerate())
        {
            foreach (var r in access)
            {
                values2.Add((identifier, BitConverter.ToInt32(r.Content)));
            }
        }
        var result = values1.Order().SequenceEqual(values2.Order());
        CollectionAssert.AreEquivalent(values1, values2);
    }
}
