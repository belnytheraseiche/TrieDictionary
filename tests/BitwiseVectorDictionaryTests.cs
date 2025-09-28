
using System.Text;
using BelNytheraSeiche.TrieDictionary;

[TestClass]
public class BitwiseVectorDictionaryTests
{

    [TestInitialize]
    public void Setup()
    {
    }

    static byte[] ToBytes(string s) => Encoding.UTF8.GetBytes(s);
    static string ToString(ReadOnlySpan<byte> b) => Encoding.UTF8.GetString(b);

    [TestMethod]
    public void Create_WithKeys_ShouldWork()
    {
        List<string> keys = ["ab", "abc", "a"];
        List<byte[]> bytes = [.. keys.Select(n => ToBytes(n))];
        var dictionary = KeyRecordDictionary.Create<BitwiseVectorDictionary>(keys);
        var result = dictionary.EnumerateAll().Select(n => ToString(n.Item2)).ToList();
        CollectionAssert.AreEquivalent(result, keys);
    }

    [TestMethod]
    public void Serialize_Deserialize_ShouldWork()
    {
        List<string> keys = ["test", "tester", "appendix", "run"];
        List<byte[]> bytes = [.. keys.Select(n => ToBytes(n))];
        var dictionary1 = KeyRecordDictionary.Create<BitwiseVectorDictionary>(keys);
        foreach (var key in bytes)
            dictionary1.GetRecordAccess(dictionary1.SearchExactly(key)).Add(key);

        var serialized1 = KeyRecordDictionary.Serialize<BitwiseVectorDictionary>(dictionary1);
        var dictionary2 = KeyRecordDictionary.Deserialize<BitwiseVectorDictionary>(serialized1);
        List<string> records = [];
        foreach (var (identifier, key) in dictionary2.EnumerateAll())
        {
            foreach (var record in dictionary2.GetRecordAccess(identifier))
                records.Add(ToString(record.Content));
        }

        var result = dictionary2.EnumerateAll().Select(n => ToString(n.Item2)).ToList();
        CollectionAssert.AreEquivalent(result, keys);
        CollectionAssert.AreEquivalent(keys, records);
    }

    [TestMethod]
    public void Serialize_With_Options_ShouldWork()
    {
        var options = new KeyRecordDictionary.SerializationOptions() { CompressionLevel = System.IO.Compression.CompressionLevel.NoCompression };
        List<string> keys = ["test", "tester", "appendix", "run"];
        List<byte[]> bytes = [.. keys.Select(n => ToBytes(n))];
        var dictionary1 = KeyRecordDictionary.Create<BitwiseVectorDictionary>(keys);
        foreach (var key in bytes)
            dictionary1.GetRecordAccess(dictionary1.SearchExactly(key)).Add(key);

        var serialized1 = KeyRecordDictionary.Serialize<BitwiseVectorDictionary>(dictionary1, options);
        var dictionary2 = KeyRecordDictionary.Deserialize<BitwiseVectorDictionary>(serialized1);
        List<string> records = [];
        foreach (var (identifier, key) in dictionary2.EnumerateAll())
        {
            foreach (var record in dictionary2.GetRecordAccess(identifier))
                records.Add(ToString(record.Content));
        }

        var result = dictionary2.EnumerateAll().Select(n => ToString(n.Item2)).ToList();
        CollectionAssert.AreEquivalent(result, keys);
        CollectionAssert.AreEquivalent(keys, records);
    }

    [TestMethod]
    public void Transient_Records_ShouldWork()
    {
        var dictionary = BitwiseVectorDictionary.Create([ToBytes("a")], KeyRecordDictionary.SearchDirectionType.LTR);
        var identifier = dictionary.SearchExactly(ToBytes("a"));
        var access = dictionary.GetRecordAccess(identifier, true);
        access.Add(ToBytes("abc")).InsertAfter(ToBytes("abe")).InsertBefore(ToBytes("abd"));
        var record0 = access.Add(ToBytes("abf"));
        List<string> result1 = [];
        foreach (var record1 in access)
            result1.Add(ToString(record1.Content));
        record0.Remove();
        List<string> result2 = [];
        foreach (var record2 in access)
            result2.Add(ToString(record2.Content));
        access.Clear();
        List<string> result3 = [];
        foreach (var record3 in access)
            result3.Add(ToString(record3.Content));

        CollectionAssert.AreEqual(result1, (string[])["abc", "abd", "abe", "abf"]);
        CollectionAssert.AreEqual(result2, (string[])["abc", "abd", "abe"]);
        CollectionAssert.AreEqual(result3, (string[])[]);
    }

    public void Persistent_Records_ShouldWork()
    {
        var dictionary = BitwiseVectorDictionary.Create([ToBytes("a")], KeyRecordDictionary.SearchDirectionType.LTR);
        var identifier = dictionary.SearchExactly(ToBytes("a"));
        var access = dictionary.GetRecordAccess(identifier, false);
        access.Add(ToBytes("abc")).InsertAfter(ToBytes("abe")).InsertBefore(ToBytes("abd"));
        var record0 = access.Add(ToBytes("abf"));
        List<string> result1 = [];
        foreach (var record1 in access)
            result1.Add(ToString(record1.Content));
        record0.Remove();
        List<string> result2 = [];
        foreach (var record2 in access)
            result2.Add(ToString(record2.Content));
        access.Clear();
        List<string> result3 = [];
        foreach (var record3 in access)
            result3.Add(ToString(record3.Content));

        CollectionAssert.AreEqual(result1, (string[])["abc", "abd", "abe", "abf"]);
        CollectionAssert.AreEqual(result2, (string[])["abc", "abd", "abe"]);
        CollectionAssert.AreEqual(result3, (string[])[]);
    }

    [TestMethod]
    public void GetKey_ShouldWork()
    {
        var key = "test";
        var bytes = ToBytes(key);
        var dictionary = BitwiseVectorDictionary.Create([bytes], KeyRecordDictionary.SearchDirectionType.LTR);
        var identifier = dictionary.SearchExactly(bytes);
        var result = dictionary.GetKey(identifier);

        CollectionAssert.AreEqual(result, bytes);
    }

    [TestMethod]
    public void EnumerateAll_ShouldReturnAllKeysInOrder()
    {
        List<string> keys = ["c", "b", "a"];
        var dictionary = BitwiseVectorDictionary.Create(keys.Select(n => ToBytes(n)), KeyRecordDictionary.SearchDirectionType.LTR);
        var result = dictionary.EnumerateAll().Select(n => ToString(n.Item2)).ToList();
        CollectionAssert.AreEquivalent(result, keys);
    }

    [TestMethod]
    public void Contains_ShouldWork()
    {
        List<string> keys = ["ape", "apple", "apps"];
        var dictionary = BitwiseVectorDictionary.Create(keys.Select(n => ToBytes(n)), KeyRecordDictionary.SearchDirectionType.LTR);
        var result1 = dictionary.Contains(ToBytes("apps"));
        var result2 = dictionary.Contains(ToBytes("abe"));
        Assert.IsTrue(result1);
        Assert.IsFalse(result2);
    }

    [TestMethod]
    public void SearchExactly_ShouldWork()
    {
        List<string> keys = ["bad", "bat", "ape", "apple", "apps"];
        var dictionary = BitwiseVectorDictionary.Create(keys.Select(n => ToBytes(n)), KeyRecordDictionary.SearchDirectionType.LTR);
        var identifier1 = dictionary.SearchExactly(ToBytes("bat"));
        var identifier2 = dictionary.SearchExactly(ToBytes("aat"));
        var key1 = dictionary.GetKey(identifier1);
        CollectionAssert.AreEqual(key1, ToBytes("bat"));
        Assert.AreEqual(identifier2, -1);
    }

    [TestMethod]
    public void SearchCommonPrefix_SearchLongestPrefix_SearchByPrefix_ShouldWork()
    {
        List<string> keys = ["app", "apps", "ape", "a", "ban"];
        var dictionary = BitwiseVectorDictionary.Create(keys.Select(n => ToBytes(n)), KeyRecordDictionary.SearchDirectionType.LTR);
        var result1 = dictionary.SearchCommonPrefix(ToBytes("appstart")).Select(n => ToString(n.Item2)).Order().ToArray();
        var result2 = ToString(dictionary.SearchLongestPrefix(ToBytes("appstart")).Item2);
        var result3 = dictionary.SearchByPrefix(ToBytes("ap")).Select(n => ToString(n.Item2)).Order().ToArray();
        CollectionAssert.AreEqual(result1, (string[])["a", "app", "apps"]);
        Assert.AreEqual(result2, "apps");
        CollectionAssert.AreEqual(result3, (string[])["ape", "app", "apps"]);
    }

    [TestMethod]
    public void SearchLongestPrefix_ShouldWork()
    {
        List<string> keys = ["a", "aa", "aaaa"];
        var dictionary = KeyRecordDictionary.Create<BitwiseVectorDictionary>(keys, Encoding.ASCII);
        var result1 = dictionary.AsStringSpecialized(Encoding.ASCII).SearchLongestPrefix("");
        var result2 = dictionary.AsStringSpecialized(Encoding.ASCII).SearchLongestPrefix("a");
        var result3 = dictionary.AsStringSpecialized(Encoding.ASCII).SearchLongestPrefix("aa");
        var result4 = dictionary.AsStringSpecialized(Encoding.ASCII).SearchLongestPrefix("aaa");
        Assert.IsTrue(result1.Item1 == -1);
        Assert.IsTrue(result2.Item2 == "a");
        Assert.IsTrue(result3.Item2 == "aa");
        Assert.IsTrue(result4.Item2 == "aa");
    }

    [TestMethod]
    public void SearchWildcard_ShouldWork()
    {
        List<string> keys = ["a", "aa", "aaa", "ab", "aba", "abc", "acc", "acd", "cca"];
        var dictionary = BitwiseVectorDictionary.Create(keys.Select(n => ToBytes(n)), KeyRecordDictionary.SearchDirectionType.LTR);
        var result1 = dictionary.SearchWildcard(ToBytes("a"), "*").Select(n => ToString(n.Item2)).ToArray();
        var result2 = dictionary.SearchWildcard(ToBytes("aba"), "*_?").Select(n => ToString(n.Item2)).ToArray();
        var result3 = dictionary.SearchWildcard(ToBytes("aa"), "*_").Select(n => ToString(n.Item2)).ToArray();
        var result4 = dictionary.SearchWildcard(ToBytes("aa"), "??").Select(n => ToString(n.Item2)).ToArray();
        var result5 = dictionary.SearchWildcard(ToBytes("aa"), "**").Select(n => ToString(n.Item2)).ToArray();
        var result6 = dictionary.SearchWildcard(ToBytes("aa"), "__").Select(n => ToString(n.Item2)).ToArray();
        var result7 = dictionary.AsStringSpecialized(Encoding.ASCII).SearchWildcard("*b?").Select(n => n.Item2).ToArray();

        var result11 = dictionary.SearchWildcard(ToBytes("a"), "*", true).Select(n => ToString(n.Item2)).ToArray();
        var result12 = dictionary.SearchWildcard(ToBytes("aba"), "*_?", true).Select(n => ToString(n.Item2)).ToArray();
        var result13 = dictionary.SearchWildcard(ToBytes("aa"), "*_", true).Select(n => ToString(n.Item2)).ToArray();
        var result14 = dictionary.SearchWildcard(ToBytes("aa"), "??", true).Select(n => ToString(n.Item2)).ToArray();
        var result15 = dictionary.SearchWildcard(ToBytes("aa"), "**", true).Select(n => ToString(n.Item2)).ToArray();
        var result16 = dictionary.SearchWildcard(ToBytes("aa"), "__", true).Select(n => ToString(n.Item2)).ToArray();
        var result17 = dictionary.AsStringSpecialized(Encoding.ASCII).SearchWildcard("*b?", reverse: true).Select(n => n.Item2).ToArray();

        CollectionAssert.AreEqual(result1, (string[])["a", "aa", "aaa", "ab", "aba", "abc", "acc", "acd", "cca"]);
        CollectionAssert.AreEqual(result2, (string[])["aba", "abc"]);
        CollectionAssert.AreEqual(result3, (string[])["a", "aa", "aaa", "aba", "cca"]);
        CollectionAssert.AreEqual(result4, (string[])["aa", "ab"]);
        CollectionAssert.AreEqual(result5, (string[])["a", "aa", "aaa", "ab", "aba", "abc", "acc", "acd", "cca"]);
        CollectionAssert.AreEqual(result6, (string[])["aa"]);
        CollectionAssert.AreEqual(result7, (string[])["aba", "abc"]);

        CollectionAssert.AreEqual(result11, (string[])["cca", "acd", "acc", "abc", "aba", "ab", "aaa", "aa", "a"]);
        CollectionAssert.AreEqual(result12, (string[])["abc", "aba"]);
        CollectionAssert.AreEqual(result13, (string[])["cca", "aba", "aaa", "aa", "a"]);
        CollectionAssert.AreEqual(result14, (string[])["ab", "aa"]);
        CollectionAssert.AreEqual(result15, (string[])["cca", "acd", "acc", "abc", "aba", "ab", "aaa", "aa", "a"]);
        CollectionAssert.AreEqual(result16, (string[])["aa"]);
        CollectionAssert.AreEqual(result17, (string[])["abc", "aba"]);
    }

    [TestMethod]
    public void FindFirst_FindNext_FindLast_FindPrevious_ShouldWork()
    {
        List<string> keys = ["bad", "app", "apps", "ape", "a", "ban"];
        var dictionary = BitwiseVectorDictionary.Create(keys.Select(n => ToBytes(n)), KeyRecordDictionary.SearchDirectionType.LTR);
        var result1 = new List<string>();
        if (dictionary.FindFirst(out var identifier2, out var key2))
        {
            result1.Add(ToString(key2));
            while (dictionary.FindNext(key2, out identifier2, out key2))
                result1.Add(ToString(key2));
        }
        var result2 = new List<string>();
        if (dictionary.FindLast(out var identifier3, out var key3))
        {
            result2.Add(ToString(key3));
            while (dictionary.FindPrevious(key3, out identifier3, out key3))
                result2.Add(ToString(key3));
        }

        CollectionAssert.AreEquivalent(result1, keys);
        CollectionAssert.AreEquivalent(result2, keys);
    }

    [TestMethod]
    public void Rtl_SearchExactly_ShouldWork()
    {
        var keys = (string[])["a", "ab", "abc"];
        var dictionary = KeyRecordDictionary.Create<BitwiseVectorDictionary>(keys, Encoding.ASCII, KeyRecordDictionary.SearchDirectionType.RTL);
        var ss = dictionary.AsStringSpecialized(Encoding.ASCII);
        var result1 = ss.SearchExactly(keys[0]);
        var result2 = ss.SearchExactly(keys[0]);
        var result3 = ss.SearchExactly(keys[0]);
        Assert.IsPositive(result1);
        Assert.IsPositive(result2);
        Assert.IsPositive(result3);
    }

    public void Rtl_GetKey_ShouldWork()
    {
        var keys = (string[])["a", "ab", "abc"];
        var dictionary = KeyRecordDictionary.Create<BitwiseVectorDictionary>(keys, Encoding.ASCII, KeyRecordDictionary.SearchDirectionType.RTL);
        var ss = dictionary.AsStringSpecialized(Encoding.ASCII);
        var result1 = ss.GetKey(ss.SearchExactly(keys[0]));
        var result2 = ss.GetKey(ss.SearchExactly(keys[0]));
        var result3 = ss.GetKey(ss.SearchExactly(keys[0]));
        Assert.IsTrue(result1 == keys[0]);
        Assert.IsTrue(result2 == keys[1]);
        Assert.IsTrue(result3 == keys[2]);
    }

    public void Rtl_SearchCommomPrefix_ShouldWork()
    {
        var keys = (string[])["a", "ba", "cba", "d"];
        var dictionary = KeyRecordDictionary.Create<BitwiseVectorDictionary>(keys, Encoding.ASCII, KeyRecordDictionary.SearchDirectionType.RTL);
        var ss = dictionary.AsStringSpecialized(Encoding.ASCII);
        var keys2 = ss.SearchCommonPrefix("edcba").Select(n => n.Item2).ToArray();
        CollectionAssert.AreEquivalent(keys, keys2);
    }

    public void Rtl_SearchLongestPrefix_ShouldWork()
    {
        var keys = (string[])["a", "ba", "cba", "d"];
        var dictionary = KeyRecordDictionary.Create<BitwiseVectorDictionary>(keys, Encoding.ASCII, KeyRecordDictionary.SearchDirectionType.RTL);
        var ss = dictionary.AsStringSpecialized(Encoding.ASCII);
        var result1 = ss.SearchLongestPrefix("edcba").Item2;
        Assert.AreEqual(result1, "cba");
    }

    public void Rtl_SearchByPrefix_ShouldWork()
    {
        var keys = (string[])["a", "ba", "cba", "d"];
        var dictionary = KeyRecordDictionary.Create<BitwiseVectorDictionary>(keys, Encoding.ASCII, KeyRecordDictionary.SearchDirectionType.RTL);
        var ss = dictionary.AsStringSpecialized(Encoding.ASCII);
        var keys2 = ss.SearchCommonPrefix("a").Select(n => n.Item2).ToArray();
        CollectionAssert.AreEquivalent(keys, keys2);
    }

    public void Rtl_EnumerateAll_ShouldWork()
    {
        var keys = (string[])["a", "ba", "cba"];
        var dictionary = KeyRecordDictionary.Create<BitwiseVectorDictionary>(keys, Encoding.ASCII, KeyRecordDictionary.SearchDirectionType.RTL);
        var ss = dictionary.AsStringSpecialized(Encoding.ASCII);
        var keys2 = ss.EnumerateAll().Select(n => n.Item2).ToArray();
        CollectionAssert.AreEquivalent(keys, keys2);
    }

    public void Rtl_FindFirst_FineNext_ShouldWork()
    {
        var keys = (string[])["a", "ba", "cba"];
        var dictionary = KeyRecordDictionary.Create<BitwiseVectorDictionary>(keys, Encoding.ASCII, KeyRecordDictionary.SearchDirectionType.RTL);
        var ss = dictionary.AsStringSpecialized(Encoding.ASCII);
        string? key1 = null;
        string? key2 = null;
        var result1 = ss.FindFirst(out var identifier, out key1);
        var result2 = false;
        if (result1)
            result2 = ss.FindNext(identifier, out var _, out key2);
        Assert.IsTrue(result1);
        Assert.IsTrue(result2);
        Assert.AreEqual(key1, "a");
        Assert.AreEqual(key2, "ba");
    }

    public void Rtl_FindLast_FindPrevious_ShouldWork()
    {
        var keys = (string[])["a", "ba", "cba"];
        var dictionary = KeyRecordDictionary.Create<BitwiseVectorDictionary>(keys, Encoding.ASCII, KeyRecordDictionary.SearchDirectionType.RTL);
        var ss = dictionary.AsStringSpecialized(Encoding.ASCII);
        string? key1 = null;
        string? key2 = null;
        var result1 = ss.FindLast(out var identifier, out key1);
        var result2 = false;
        if (result1)
            result2 = ss.FindPrevious(identifier, out var _, out key2);
        Assert.IsTrue(result1);
        Assert.IsTrue(result2);
        Assert.AreEqual(key1, "cba");
        Assert.AreEqual(key2, "ba");
    }
}
