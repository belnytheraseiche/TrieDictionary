
using System.Text;
using BelNytheraSeiche.TrieDictionary;

[TestClass]
public class PrimitiveRecordStoreTests
{
    [TestInitialize]
    public void Setup()
    {
    }

    [TestMethod]
    public void RecordAccess_Add_Insert_Remove_Clear_ShouldWork()
    {
        var store = new PrimitiveRecordStore();
        var access1 = store.GetRecordAccess();
        var r1 = access1.Add("a"u8).InsertAfter("b"u8).InsertAfter("c"u8);
        var r2 = access1.Add("d"u8);
        var r3 = r1.InsertBefore("e"u8);
        r2.InsertAfter("f"u8);
        r3.Remove();
        var values1 = new List<string>();
        foreach (var r in access1)
            values1.Add(Encoding.UTF8.GetString(r.Read()));
        var result1 = String.Concat(values1);

        var access2 = store.GetRecordAccess();
        access2.Add("A"u8).InsertAfter("B"u8);
        access2.First().InsertBefore("C"u8);
        var values2 = new List<string>();
        foreach (var r in access2)
            values2.Add(Encoding.UTF8.GetString(r.Read()));
        var result2 = String.Concat(values2);

        var access3 = store.GetRecordAccess();
        access3.Add("A"u8).InsertAfter("B"u8);
        access3.First().InsertBefore("C"u8);
        access3.First().Remove();
        var values3 = new List<string>();
        foreach (var r in access3)
            values3.Add(Encoding.UTF8.GetString(r.Read()));
        var result3 = String.Concat(values3);

        var access4 = store.GetRecordAccess();
        access4.Add("a"u8).InsertAfter("b"u8);
        access4.Clear();
        var result4 = access4.FirstOrDefault();

        var access5 = store.GetRecordAccess();
        access5.Add("a"u8).InsertAfter("b"u8);
        while (access5.FirstOrDefault() != null)
            access5.First().Remove();
        access5.Add("x"u8);
        var result5 = Encoding.UTF8.GetString(access5.First().Read());

        Assert.AreEqual(result1, "abcdf");
        Assert.AreEqual(result2, "CAB");
        Assert.AreEqual(result3, "AB");
        Assert.AreEqual(result4, null);
        Assert.AreEqual(result5, "x");
    }

    [TestMethod]
    public void Serialize_Deserialize_ShouldWork()
    {
        var store1 = new PrimitiveRecordStore();
        var identifiers = new List<int>();
        for (var i = 0; i < 10000; i++)
        {
            var access = store1.GetRecordAccess();
            access.Add(BitConverter.GetBytes(i)).InsertAfter(BitConverter.GetBytes(access.Identifier));
            identifiers.Add(access.Identifier);
        }
        var serialized = PrimitiveRecordStore.Serialize(store1);
        var store2 = PrimitiveRecordStore.Deserialize(serialized);
        var result = false;
        try
        {
            var index = 0;
            foreach (var identifier in identifiers)
            {
                var access = store2.GetRecordAccess(identifier);
                var r1 = access.First();
                var r2 = r1.Next!;
                if (index != BitConverter.ToInt32(r1.Read()))
                    throw new Exception();
                if (identifier != BitConverter.ToInt32(r2.Read()))
                    throw new Exception();

                index++;
            }
            result = true;
        }
        catch
        {
        }

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void Serialize_With_Options_ShouldWork()
    {
        var store1 = new PrimitiveRecordStore();
        var identifiers = new List<int>();
        for (var i = 0; i < 10000; i++)
        {
            var access = store1.GetRecordAccess();
            access.Add(BitConverter.GetBytes(i)).InsertAfter(BitConverter.GetBytes(access.Identifier));
            identifiers.Add(access.Identifier);
        }
        var options = new PrimitiveRecordStore.SerializationOptions() { CompressionLevel = System.IO.Compression.CompressionLevel.NoCompression };
        var serialized = PrimitiveRecordStore.Serialize(store1, options);
        var store2 = PrimitiveRecordStore.Deserialize(serialized);
        var result = false;
        try
        {
            var index = 0;
            foreach (var identifier in identifiers)
            {
                var access = store2.GetRecordAccess(identifier);
                var r1 = access.First();
                var r2 = r1.Next!;
                if (index != BitConverter.ToInt32(r1.Read()))
                    throw new Exception();
                if (identifier != BitConverter.ToInt32(r2.Read()))
                    throw new Exception();

                index++;
            }
            result = true;
        }
        catch
        {
        }

        Assert.IsTrue(result);
    }
}
