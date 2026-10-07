// Prove it: an exception from an async void method cannot reach its caller, and it ends the process.
try
{
    await SaveAsync(-1);                       // async Task: the exception travels inside the Task
}
catch (ArgumentOutOfRangeException e)
{
    Console.WriteLine($"async Task: the caller caught {e.GetType().Name}");
}

try
{
    Save(-1);                                  // async void: there is no Task to carry the exception
    Console.WriteLine("async void: the call returned normally and the catch below never ran");
}
catch (ArgumentOutOfRangeException e)
{
    Console.WriteLine($"async void: the caller caught {e.GetType().Name}");   // never printed
}

await Task.Delay(1000);                        // the process dies in here, on a thread-pool thread
Console.WriteLine("still alive");              // never printed

static async Task SaveAsync(int id) { ArgumentOutOfRangeException.ThrowIfNegative(id); await Task.Delay(10); }

static async void Save(int id) { ArgumentOutOfRangeException.ThrowIfNegative(id); await Task.Delay(10); }
