// Prove it: a LINQ query is a recipe. It runs again on every enumeration and reads captured variables when it runs.
int calls = 0, minimum = 2;
int[] numbers = [1, 2, 3, 4];
IEnumerable<int> query = numbers.Where(n => { calls++; return n >= minimum; });
Console.WriteLine($"query defined:        the predicate ran {calls} times");

int count = query.Count();                     // enumeration 1
int sum = query.Sum();                         // enumeration 2: the whole pipeline runs again
Console.WriteLine($"Count() and Sum():    the predicate ran {calls} times (count {count}, sum {sum})");

calls = 0;
List<int> list = query.ToList();               // one enumeration; from here on, a plain list
Console.WriteLine($"ToList(), then both:  the predicate ran {calls} times (count {list.Count}, sum {list.Sum()})");

minimum = 4;                                   // the lambda captured the variable, not its value 2
Console.WriteLine($"after minimum = 4:    query [{string.Join(", ", query)}], list [{string.Join(", ", list)}]");

IQueryable<int> queryable = numbers.AsQueryable().Where(n => n >= minimum);
Console.WriteLine($"an IQueryable holds an expression tree: {queryable.Expression}");
