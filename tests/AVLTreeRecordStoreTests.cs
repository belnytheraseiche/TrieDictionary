
using System.Text;
using BelNytheraSeiche.TrieDictionary;

[TestClass]
public class AVLTreeRecordStoreTests
{
    AVLTreeRecordStore store_ = new();

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
        var store1 = new AVLTreeRecordStore();
        var values1 = new List<(int, int)>();
        for (var i = 0; i < 10000; i++)
        {
            var identifier = Random.Shared.Next(1000);
            var access = store1.GetRecordAccess(identifier);
            access.Add(BitConverter.GetBytes(i));
            values1.Add((identifier, i));
        }
        var serialized = BasicRecordStore.Serialize(store1);
        var store2 = BasicRecordStore.Deserialize<AVLTreeRecordStore>(serialized);
        var values2 = new List<(int, int)>();
        foreach (var (identifier, access) in store2.Enumerate())
        {
            foreach (var r in access)
            {
                values2.Add((identifier, BitConverter.ToInt32(r.Content)));
            }
        }
        CollectionAssert.AreEquivalent(values1, values2);
    }

    [TestMethod]
    public void Serialize_With_Options_ShouldWork()
    {
        var store1 = new AVLTreeRecordStore();
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
        var store2 = BasicRecordStore.Deserialize<AVLTreeRecordStore>(serialized);
        var values2 = new List<(int, int)>();
        foreach (var (identifier, access) in store2.Enumerate())
        {
            foreach (var r in access)
            {
                values2.Add((identifier, BitConverter.ToInt32(r.Content)));
            }
        }
        CollectionAssert.AreEquivalent(values1, values2);
    }

    [TestMethod]
    public void RecordAccess_Add_Insert_Remove_Clear_ShouldWork()
    {
        var access1 = store_.GetRecordAccess(9);
        var r1 = access1.Add("a"u8).InsertAfter("b"u8).InsertAfter("c"u8);
        var r2 = access1.Add("d"u8);
        var r3 = r1.InsertBefore("e"u8);
        r2.InsertAfter("f"u8);
        r3.Remove();
        var values1 = new List<string>();
        foreach (var r in access1)
            values1.Add(Encoding.UTF8.GetString(r.Content));
        var result1 = String.Concat(values1);

        var access2 = store_.GetRecordAccess(8);
        access2.Add("A"u8).InsertAfter("B"u8);
        access2.First().InsertBefore("C"u8);
        var values2 = new List<string>();
        foreach (var r in access2)
            values2.Add(Encoding.UTF8.GetString(r.Content));
        var result2 = String.Concat(values2);

        var access3 = store_.GetRecordAccess(7);
        access3.Add("A"u8).InsertAfter("B"u8);
        access3.First().InsertBefore("C"u8);
        access3.First().Remove();
        var values3 = new List<string>();
        foreach (var r in access3)
            values3.Add(Encoding.UTF8.GetString(r.Content));
        var result3 = String.Concat(values3);

        var access4 = store_.GetRecordAccess(6);
        access4.Add("a"u8).InsertAfter("b"u8);
        access4.Clear();
        var result4 = access4.FirstOrDefault();

        var access5 = store_.GetRecordAccess(5);
        access5.Add("a"u8).InsertAfter("b"u8);
        while (access5.FirstOrDefault() != null)
            access5.First().Remove();
        access5.Add("x"u8);
        var result5 = Encoding.UTF8.GetString(access5.First().Content);

        Assert.AreEqual(result1, "abcdf");
        Assert.AreEqual(result2, "CAB");
        Assert.AreEqual(result3, "AB");
        Assert.AreEqual(result4, null);
        Assert.AreEqual(result5, "x");
    }
}
