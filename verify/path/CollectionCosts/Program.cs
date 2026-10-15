// Prove it: a List<T> doubles its array as it grows; a hash set finds a key by its hash and loses a key whose hash changed.
var list = new List<Key>();
var capacities = new List<int>();
for (int i = 0; i < 10_000; i++)
{
    list.Add(new Key(i));
    if (capacities.Count == 0 || capacities[^1] != list.Capacity) capacities.Add(list.Capacity);
}
Console.WriteLine($"capacities while adding 10,000: {string.Join(" ", capacities)}");

var set = new HashSet<Key>(list);
var probe = new Key(9_999);
Key.EqualsCalls = 0; list.Contains(probe);
Console.WriteLine($"list.Contains(9999): {Key.EqualsCalls} Equals calls (a scan)");
Key.EqualsCalls = 0; set.Contains(probe);
Console.WriteLine($"set.Contains(9999):  {Key.EqualsCalls} Equals call (hash, bucket, one compare)");

Key stored = list[42];
stored.Id = 1_000_042;                       // mutate a key while it is in the set
Console.WriteLine($"after mutating key 42: set.Contains(it) {set.Contains(stored)}, set.Count {set.Count}, " +
                  $"still there by reference: {set.Any(k => ReferenceEquals(k, stored))}");

sealed class Key(int id)
{
    public static int EqualsCalls;
    public int Id = id;
    public override bool Equals(object? obj) { EqualsCalls++; return obj is Key other && other.Id == Id; }
    public override int GetHashCode() => Id;
}
