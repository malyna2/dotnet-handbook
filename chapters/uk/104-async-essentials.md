# Розділ 4: Основи асинхронності

У цьому розділі розглянуто, як `async` і `await` впливають на три речі: потік, exceptions (винятки) й роботу, яка очікує. Кожен збій асинхронності в продакшені пов’язаний з однією з них. Після розділу ви зможете писати й перевіряти асинхронний код запитів так, щоб не губити exceptions й не займати зайві потоки: знатимете, куди потрапляє exception з `async void` чи `Task.WhenAll`, чому `.Result` сповільнює кожну endpoint (кінцева точка) навіть без deadlock (взаємне блокування), що насправді зупиняє `CancellationToken` і як спільно використовувати стан між потоками без гонки.

Усе пов’язує один механізм: **`await` звільняє потік і призупиняє решту методу до завершення задачі; задача — єдиний об’єкт, який повертає результат або exception.** Розділи послідовно розвивають цю модель. Спершу з’ясуємо, навіщо потрібна асинхронність і як працює thread pool (пул потоків); далі — що таке `Task` і що робить із нею `await`, аж до згенерованої компілятором state machine (машина станів). Потім розглянемо, де виконується решта методу після завершення задачі й що стається, коли потік блокується в очікуванні: класичне deadlock та його тихіший і поширеніший родич — виснаження thread pool. Далі — як зупиняти непотрібну роботу (скасування) і як запускати кілька операцій одночасно (`WhenAll`, `WhenAny`, обмеження паралельності, асинхронні потоки). Паралельна робота передбачає спільні дані, тому завершимо розділ потокобезпечністю.

`ValueTask`, паралельні цикли TPL, канали й правила поповнення thread pool розвивають цю модель і розглядаються в [Розділі 17: Внутрішня будова runtime (середовище виконання) й продуктивність](#chapter-17-runtime-internals-and-performance). У [Розділ 5: HTTP і веб-API](#chapter-5-http-and-web-apis) її застосовано до вебзапитів.

## Навіщо потрібна асинхронність: робота з обмеженням процесора та введення-виведення

**Робота з обмеженням процесора** завантажує ядро: хешування пароля, змінення розміру зображення, підсумовування мільярда чисел. Щоб виконувати більше такої роботи водночас, потрібні додаткові ядра.

**Робота з обмеженням введення-виведення** майже весь час *очікує*: на запит до бази даних, виклик HTTP чи читання файла. Поки триває очікування, жодне ядро не працює для вас.

**Асинхронність дає змогу не марнувати потоки під час очікування введення-виведення**, а не виконувати кілька операцій водночас. Добре написаний асинхронний виклик введення-виведення не використовує *жодного* потоку, поки чекає: операцію завершує операційна система, а runtime отримує сповіщення. Модель «асинхронність означає виконання в іншому потоці» — перше, від чого варто відмовитися.

### Потоки мають значні витрати

Кожен потік заздалегідь резервує адресний простір для власного стека (мегабайти; типове значення залежить від ОС) і потребує об’єкта ядра. Під час кожного перемикання контексту зберігаються регістри, а кеші процесора втрачають актуальність. Схема «один потік на запит» ламається вже за кількох тисяч одночасних з’єднань. Асинхронність дає змогу обслуговувати 10 000 запитів, що чекають на повільну базу даних, лише кількома потоками: потік, який інакше чекав би, повертається до іншої роботи.

### Thread pool

`Task.Run`, callbacks (зворотні виклики) таймерів і кожне продовження після `await` виконуються в **thread pool** — наборі повторно використовуваних робочих потоків, розмір якого змінюється, а не є фіксованим:

- **До мінімального розміру** — типово один потік на ядро — пул створює потоки на вимогу.
- **Понад мінімум пул зростає обережно.** Алгоритм *підйому схилом* додає чи вилучає потоки, відстежуючи пропускну здатність, а *детектор голодування* додає потік, якщо поставлена в queue (черга) робота не просувалася приблизно пів секунди.

Таке обережне зростання правильне для роботи з обмеженням процесора, де кількість потоків понад кількість ядер лише збільшує витрати на перемикання, але згубне для заблокованих потоків ([Deadlock через синхронне очікування асинхронного коду](#the-sync-over-async-deadlock)).

> **Рекомендація.** Не створюйте звичайні об’єкти `Thread` для типових завдань. Використовуйте пул (`Task.Run` для роботи з обмеженням процесора) або, ще краще, справжнє асинхронне введення-виведення, яке не займає потоку під час очікування.

## Задачі: обіцянка майбутнього результату

`Task` — це обіцянка майбутнього результату або помилки; `Task<TResult>` містить значення. Задача завершується зі станом `RanToCompletion`, `Faulted` (із exception) або `Canceled`, а приєднані до неї *продовження* запускаються після завершення. `async`/`await` — переважно синтаксичний рівень над цим механізмом.

## `async`/`await` докладно

```csharp
public async Task<int> GetUserAgeAsync(int userId)
{
    User user = await _repository.GetUserAsync(userId);   // I/O: database
    int age = CalculateAge(user.BirthDate);               // CPU: trivial
    return age;
}
```

**`await` не блокує потік.** Точніше було б сказати: «передай керування, доки не буде готово».

### Що насправді робить `await`

Коли виконання доходить до `await _repository.GetUserAsync(userId)`:

1. `GetUserAsync` запускає операцію з базою даних і повертає `Task<User>`, яка *ще не завершена*.
2. Якщо задача вже завершилася (наприклад, результат є в кеші), виконання одразу продовжується без призупинення.
3. Інакше метод **призупиняється**: реєструється *продовження* — «після завершення цієї задачі виконай решту `GetUserAgeAsync`» — і метод **повертається до викликувача**. Потік звільняється.

Жоден потік не заблоковано на очікуванні відповіді бази даних. Коли відповідь надходить, продовження ставиться в queue thread pool (або передається захопленому `SynchronizationContext`), а потік — можливо, інший — продовжує виконання після `await` зі заповненою змінною `user`.

### Згенерована компілятором state machine

Метод може призупинитися, а потім продовжити роботу в іншому потоці, тому що компілятор перетворює його на **state machine**, яка зберігає локальні змінні в полях, а не в стеку. Спрощений вигляд:

```csharp
private struct GetUserAgeStateMachine : IAsyncStateMachine
{
    public int state;                                 // where we paused
    public AsyncTaskMethodBuilder<int> builder;       // drives the returned Task
    public UserRepository repository;
    public int userId;                                // hoisted parameter

    private TaskAwaiter<User> userAwaiter;            // hoisted local

    public void MoveNext()
    {
        int age;
        try
        {
            if (state == -1) // first entry
            {
                userAwaiter = repository.GetUserAsync(userId).GetAwaiter();
                if (!userAwaiter.IsCompleted)
                {
                    state = 0;
                    // Schedule MoveNext to run again when the task completes,
                    // then return control to the caller.
                    builder.AwaitUnsafeOnCompleted(ref userAwaiter, ref this);
                    return;
                }
            }
            else // state == 0: we were resumed after the await
            {
                state = -1;
            }

            User user = userAwaiter.GetResult(); // get result OR rethrow exception
            age = CalculateAge(user.BirthDate);
        }
        catch (Exception ex)
        {
            state = -2;
            builder.SetException(ex);   // faults the returned Task
            return;
        }

        state = -2;
        builder.SetResult(age);         // completes the returned Task
    }
}
```

Це пояснює майже всі несподівані властивості асинхронного коду:

- **Локальні змінні стали полями** (`userAwaiter`, `userId`), тож переживають призупинення. Асинхронний метод виділяє пам’ять, якщо справді призупиняється.
- **`MoveNext` виконується частинами.** Кожен виклик працює до наступного незавершеного `await` і повертає керування; після відновлення `state` указує місце продовження.
- **Exceptions виникають на `GetResult()`** і повторно викидаються на рядку `await` без обгортки `AggregateException`.
- **Усе тіло методу міститься в одному `try/catch`.** Кожен exception, навіть викинутий до першого `await`, потрапляє до `builder.SetException`, а не безпосередньо до викликувача.
- **`AwaitUnsafeOnCompleted` налаштовує продовження** й захоплює `SynchronizationContext`.
- **`builder` створює `Task`, яку отримав викликувач**, і завершує її результатом або exception.

Очікуваним може бути будь-який тип із `GetAwaiter()`, результат якого має `IsCompleted`, `OnCompleted` і `GetResult`. Саме тому працюють `Task`, `ValueTask` і власні типи.

> **Зверніть увагу.** **Чому exception з `async void` не можна перехопити — і чому він завершує процес.**
>
> - **У exception немає каналу.** `async void` не повертає `Task`, тож exception нікуди передатися. `catch` state machine усе одно передає його будівнику, тому виклик завершується звичайно, а `try/catch` викликувача не виконується.
> - **Будівник викидає його там, де його ніхто не очікує.** `AsyncVoidMethodBuilder.SetException` повторно викидає exception у захопленому `SynchronizationContext` (диспетчері інтерфейсу користувача). Якщо контексту немає — в ASP.NET Core, фоновій службі чи консольній програмі — exception викидається в потоці пулу.
> - **У цього потоку немає викликувача, який міг би його перехопити.** Exception залишається необробленим, і runtime завершує процес: у вебзастосунку гинуть усі запити, а не лише невдалий.
>
> Поверніть `Task`. `async void` дозволений лише для handlers (обробники) events (події), сигнатуру яких задає платформа; такий handler має сам перехоплювати всі exceptions.

## `SynchronizationContext` і `ConfigureAwait`

Деякі продовження *мають* виконуватися в конкретному потоці: у настільному інтерфейсі лише потік інтерфейсу може змінювати елементи керування. `SynchronizationContext` відповідає на запитання: «де має виконатися це продовження?». `await` захоплює поточний контекст (або, якщо його немає, поточний `TaskScheduler`) і повертає продовження до нього.

- **WPF / WinForms:** контекст інтерфейсу передає продовження потоку інтерфейсу, тому `await FetchAsync(); label.Text = result;` безпечно.
- **ASP.NET Core:** **`SynchronizationContext` відсутній**; продовження виконуються в будь-якому потоці пулу. (У класичному ASP.NET контекст був прив’язаний до запиту й спричиняв описані далі deadlocks.)
- **Консольні застосунки:** типово контексту немає; продовження виконуються в пулі.

### `ConfigureAwait(false)`

`ConfigureAwait(false)` означає: «мені байдуже, який потік продовжить роботу; не повертай мене до захопленого контексту».

```csharp
public async Task<byte[]> DownloadAndHashAsync(string url)
{
    byte[] data = await _httpClient.GetByteArrayAsync(url).ConfigureAwait(false);
    // Resumes on a thread pool thread, NOT the original context.
    return SHA256.HashData(data);
}
```

- **Код бібліотек: використовуйте його майже після кожного `await`.** Ви не знаєте контексту викликувача й не потребуєте його; відмова від повернення до контексту швидша й не дає бібліотеці спричинити наведене далі deadlock.
- **Handlers events інтерфейсу: не використовуйте**, якщо після `await` код змінює інтерфейс.
- **В ASP.NET Core це нічого не змінює з погляду коректності** — повертатися до контексту нікуди. Не покладайтеся на нього як на засіб виправлення проблем.

> **Пастка.** `ConfigureAwait(false)` впливає лише на *один* `await`, до якого його застосовано. Кожен `await` окремо вирішує, чи захоплювати контекст, тому застосовуйте його послідовно.

> **.NET 8 — `ConfigureAwaitOptions`.** Перевантаження `ConfigureAwait(ConfigureAwaitOptions)` приймає перелік `[Flags]`:
>
> - `ContinueOnCapturedContext` — старе значення `true`;
> - `SuppressThrowing` — дочекатися завершення, не спостерігаючи exception; корисно для запуску без очікування, результат якого перевірите деінде;
> - `ForceYielding` — завжди призупиняти виконання, навіть якщо задача вже завершена.
>
> `SuppressThrowing` працює лише зі звичайним `Task`. Для `Task<T>` він викидає `ArgumentOutOfRangeException`, тож спершу перетворіть її на `Task`. Для `ValueTask` цієї можливості немає.

## Deadlock через синхронне очікування асинхронного коду

Найвідоміша помилка асинхронного коду — **синхронне очікування асинхронної операції**: потік блокується в очікуванні її завершення.

```csharp
// DANGER: do not do this
public string GetData()
{
    return GetDataAsync().Result;  // blocks the current thread
}
```

У однопотоковому `SynchronizationContext` (класичному ASP.NET або потоці інтерфейсу WPF/WinForms) послідовність стає фатальною:

1. Потік інтерфейсу викликає `GetData()`, та викликає `GetDataAsync()` і блокується на `.Result`.
2. Усередині `GetDataAsync` виклик `await SomethingAsync()` захоплює `SynchronizationContext` інтерфейсу.
3. `SomethingAsync` завершується; її продовження треба повернути **в потік інтерфейсу**.
4. Потік інтерфейсу заблоковано на кроці 1, і він не обробляє цикл повідомлень, який мав би запустити продовження.
5. Отже, `GetDataAsync` не завершується, а `.Result` не повертається. **Deadlock:** потік чекає на результат, який може створити лише він сам.

Є два способи розірвати цей цикл, але справжнім виправленням є лише один:

- **Справжнє виправлення: асинхронний код до самого низу** — `public async Task<string> GetData() => await GetDataAsync();`
- Частково допомагає `ConfigureAwait(false)` усередині `GetDataAsync`: тоді продовженню не потрібен потік інтерфейсу. Але ви не завжди контролюєте весь ланцюжок, і це не усуває виснаження пулу.

> **Зверніть увагу.** **Виснаження без deadlock: чому `.Result` шкодить і в ASP.NET Core.**

> **Механізм.** За відсутності `SynchronizationContext` продовженню потрібен *будь-який* потік пулу, а не саме цей. Під навантаженням усі потоки пулу заблоковані на `.Result`, а продовження, які мали б їх розблокувати, разом із таймерами та callbacks введення-виведення стоять у queue пулу. Їх звільняють лише нові потоки, а пул додає їх обережно:
>
> - **Про блокування пулу надходить сповіщення.** Починаючи з .NET 6, потік пулу, заблокований на `Task.Wait` (його також використовують `.Result` і `.GetAwaiter().GetResult()`), сповіщає пул, який компенсує блокування. У .NET 10 він одразу додає до одного потоку на ядро. Далі додає потоки по одному, чекаючи 25 мс перед кожним і збільшуючи це очікування ще на 25 мс після кожної наступної групи з одного потоку на ядро, аж до 250 мс для потоку.
> - **Про блокування не повідомляється** — `Thread.Sleep`, `SemaphoreSlim.Wait`, блокування на `lock` — тоді працює лише детектор голодування, який додає приблизно один потік за пів секунди.
>
> **Як це проявляється.** Пропускна здатність падає до швидкості додавання потоків, затримка зростає на *кожній* endpoint, а завантаження процесора залишається низьким, бо потоки чекають, а не працюють. У `dotnet-counters` зростає довжина queue пулу, а кількість потоків постійно збільшується. Практичне завдання наприкінці розділу вимірює це.

> **Рекомендація.** Ніколи не блокуйте асинхронний код через `.Result`, `.Wait()` чи `.GetAwaiter().GetResult()` у коді застосунку. На межі, де синхронний код неможливо змінити (конструктор або інтерфейс), ізолюйте блокування й враховуйте його вартість.

## `CancellationToken`: кооперативне скасування

Безпечно примусово зупинити запущену операцію неможливо, тому скасування **кооперативне**: `CancellationToken` передається операції, яка *сама вирішує* перевіряти його й зупинятися. Той, хто може скасувати роботу, зберігає `CancellationTokenSource`; consumer (споживач) отримують його `Token`.

```csharp
public async Task<Report> GenerateReportAsync(CancellationToken cancellationToken)
{
    var rows = new List<Row>();
    await foreach (Row row in _db.StreamRowsAsync(cancellationToken))
    {
        cancellationToken.ThrowIfCancellationRequested(); // honor the signal
        rows.Add(Transform(row));
    }
    return new Report(rows);
}

// Caller with a timeout:
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
try
{
    Report report = await GenerateReportAsync(cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Report generation timed out.");
}
```

- **Передавайте токен далі** (за домовленістю — останнім параметром) у кожен асинхронний виклик.
- **Враховуйте його.** У щільних циклах викликайте `token.ThrowIfCancellationRequested()`; API платформи (HttpClient, EF Core, потоки) перевіряють переданий токен.
- **Тайм-аути** задаються через `CancellationTokenSource`, створений із затримкою, або через `CancelAfter`.
- **Пов’язані токени** поєднують джерела — «скасувати, якщо клієнт перервав запит *або* сплив наш 10-секундний ліміт»:

```csharp
using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
    requestAborted, timeoutCts.Token);
await DoWorkAsync(linkedCts.Token); // cancels when EITHER source fires
```

Скасування проявляється як `OperationCanceledException` (або його підтип `TaskCanceledException`): це очікуване керування потоком, а не помилка, тож не записуйте його в журнал як збій.

> **Зверніть увагу.** **Токен зупиняє лише ті виклики, до яких його передано.**
>
> Скасування кооперативне: `Cancel()` встановлює прапорець і запускає зареєстровані в токені callbacks — і нічого більше. Код зупиниться лише там, де перевірить цей прапорець, або де переданий токен отримав API. SqlClient реєструє callback, що скасовує команду на сервері, а `HttpClient` перериває запит. Якщо в ланцюжку є метод без токена або він не передає його далі, нижча робота триватиме до завершення.
>
> Виправлення: приймайте `CancellationToken` у кожному асинхронному методі на шляху запиту й передавайте його далі. Аналізатор CA2016 позначає виклик, якому можна передати доступний токен; у .NET 10 за замовчуванням це лише рекомендація, тож налаштуйте для нього рівень попередження у `.editorconfig`.

У веб-API runtime надає кожній endpoint токен, який спрацьовує після розриву з’єднання клієнтом. У [Поширенні `CancellationToken`](#cancellationtoken-propagation) в Розділ 5 показано, що дає його передавання далі й коли цього робити не слід.

> **Пастка.** Звільняйте `CancellationTokenSource` (це робить `using`). `CancellationTokenSource(TimeSpan)` запускає таймер, і незвільнене джерело тримає його зареєстрованим до спрацювання.

## Поєднання паралельної роботи: `WhenAll` і `WhenAny`

Асинхронність найкраще проявляється, коли незалежні операції введення-виведення виконуються *паралельно*, а не одна за одною:

```csharp
// SLOW: three round-trips back to back, ~300ms total
var a = await GetAAsync();
var b = await GetBAsync();
var c = await GetCAsync();
```

Якщо виклики не залежать один від одного, запустіть їх усі, а потім дочекайтеся завершення разом:

```csharp
// FAST: three round-trips in flight at once, ~100ms total
Task<A> ta = GetAAsync();
Task<B> tb = GetBAsync();
Task<C> tc = GetCAsync();
await Task.WhenAll(ta, tb, tc);
var result = new Combined(ta.Result, tb.Result, tc.Result); // safe: all completed
```

Задачі *запускаються* до першого очікування, тому виконуються паралельно; після успішного `WhenAll` доступ до `.Result` не блокує потік.

### Exception handling у `WhenAll`

Якщо кілька задач, переданих у `WhenAll`, завершилися з помилкою, повернена задача містить `AggregateException` з *усіма* exceptions, але `await` повторно викидає лише **один**. Щоб побачити кожну помилку, збережіть задачу `WhenAll` і прочитайте її в `catch`:

```csharp
Task all = Task.WhenAll(task1, task2, task3);
try
{
    await all;                                   // rethrows ONE of the exceptions
}
catch (Exception)
{
    if (all.Exception is { } failures)           // null if the tasks were cancelled, not faulted
        foreach (Exception ex in failures.InnerExceptions)
            _logger.LogError(ex, "A task failed");
    throw;
}
```

> **Зверніть увагу.** **Який exception викидає `await` і чому лише один.**
>
> - **Лише один — навмисно.** `await` викликає `GetResult()`, який повторно викидає *перший* збережений задачею exception через `ExceptionDispatchInfo`, зберігаючи початковий call stack (стек викликів) ([Механізм, що має значення](#the-mechanics-that-bite) у Розділ 9 розглядає цей інструмент). Асинхронний код має читатися як синхронний, де виклик викидає один exception.
> - **Блокувальні виклики викидають обгортку.** `.Wait()` і `.Result` викидають сам `AggregateException`.
> - **«Перший» залежить від перевантаження.** Починаючи з .NET 8, `WhenAll` для звичайних `Task` перелічує помилки в порядку їх виникнення; реалізація .NET 7 обходила задачі в порядку аргументів. Для `Task<T>` і далі використовується порядок аргументів.
>
> Не покладайтеся на конкретний exception: записуйте в журнал `InnerExceptions`, як у прикладі.

`Task.WhenAny` завершується разом із *першою* задачею — це підхід «перемагає перша відповідь» або гонка з тайм-аутом. Він повертає саме цю *задачу*; її можна очікувати, щоб отримати результат чи exception.

> **Пастка.** За використання `WhenAny` задачі, що не перемогли, продовжують виконуватися. Якщо одна з них пізніше завершиться з помилкою, а ніхто її не спостерігатиме, exception загубиться. Дочекайтеся решти або врахуйте їх іншим способом.

Обробка результатів у циклі `WhenAny` має складність O(n²), адже кожна ітерація знову переглядає решту. У .NET 9 `Task.WhenEach` видає задачі в порядку завершення: `await foreach (var task in Task.WhenEach(tasks)) { ... }`.

### Обмеження паралельності через `SemaphoreSlim`

Запуск 10 000 HTTP-викликів через `Task.WhenAll` перевантажить віддалений сервер і вичерпає доступні сокети. `SemaphoreSlim` обмежує кількість одночасних операцій:

```csharp
public async Task<IReadOnlyList<Result>> FetchAllAsync(IEnumerable<string> urls)
{
    using var gate = new SemaphoreSlim(initialCount: 8); // max 8 in flight
    var tasks = urls.Select(async url =>
    {
        await gate.WaitAsync();            // acquire a slot (async, no blocking)
        try { return await FetchAsync(url); }
        finally { gate.Release(); }        // ALWAYS release
    });
    return await Task.WhenAll(tasks);
}
```

> **Пастка.** Звільняйте семафор у `finally`. Якщо exception омине `Release`, дозвіл буде втрачено назавжди, і запас дозволів поступово вичерпається до deadlock. В асинхронному коді використовуйте `WaitAsync`, ніколи — блокувальний `Wait`.

## `IAsyncEnumerable` й асинхронні потоки

`Task<List<T>>` повертає все й лише після завершення всієї роботи; `IAsyncEnumerable<T>` видає елементи по одному в міру надходження — це асинхронний потік, який створюють через `async` і `yield return`:

```csharp
public async IAsyncEnumerable<Trade> ReadTradesAsync(
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    await using var reader = await _source.OpenAsync(cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
        yield return Map(reader.Current); // one item, lazily, as it's read
    }
}

// Consume with await foreach:
await foreach (Trade trade in ReadTradesAsync(cancellationToken))
{
    Process(trade);
}
```

`[EnumeratorCancellation]` дає токену, переданому через `WithCancellation`, потрапити до параметра ітератора:

```csharp
await foreach (var trade in source.ReadTradesAsync().WithCancellation(cancellationToken))
    Process(trade);
```

Асинхронні потоки зручні для посторінкового читання великих наборів даних і обробки рядків без завантаження всього в пам’ять. Кожна ітерація може призупинити метод і звільнити потік, як будь-який `await`.

## Потокобезпечність: спільний стан без помилок

Усе описане вище запускає роботу паралельно: задачі, запущені до очікування, і продовження, що відновлюються в будь-якому вільному потоці пулу. Додайте паралельні цикли з [Розділу 17](#chapter-17-runtime-internals-and-performance), і два потоки зможуть водночас змінювати ті самі дані.

### Гонки даних

```csharp
_counter++; // NOT atomic
```

Операція складається з читання, додавання одиниці й запису результату: якщо два потоки перемкнуться між цими кроками, обидва прочитають `5` і обидва запишуть `6`. Два збільшення, а зараховане лише одне — це **гонка даних**.

### `lock`

Оператор `lock` дозволяє лише одному потоку виконувати критичну секцію в певний момент:

```csharp
private readonly Lock _sync = new();   // .NET 9+; lock a private object on older targets
private int _counter;

public void Increment()
{
    lock (_sync)      // mutual exclusion
    {
        _counter++;
    }
}
```

У .NET 9 і C# 13 використовуйте для блокування `System.Threading.Lock`: компілятор застосовує його швидший API. Для старіших платформ використовуйте окремий `object`.

> **Пастка.**
> - **Ніколи не блокуйте `this`, `Type` або рядок.** Зовнішній код може заблокувати той самий екземпляр і спричинити deadlock. Використовуйте окремий приватний об’єкт лише для читання.
> - **Ніколи не використовуйте `await` усередині `lock`.** Блокування прив’язане до потоку: звільнити його має той самий потік, який узяв; але продовження після `await` може виконатися в іншому потоці. Компілятор і так забороняє це. Для асинхронного взаємного виключення використовуйте `SemaphoreSlim(1, 1)` із `WaitAsync`.
> - **Зменшуйте критичні секції**, щоб мінімізувати конкуренцію за блокування.

### `Interlocked`

Для однієї атомарної операції блокування надлишкове. `Interlocked` надає атомарні примітиви без блокувань, реалізовані інструкціями процесора:

```csharp
Interlocked.Increment(ref _counter);          // atomic ++
Interlocked.Add(ref _total, amount);          // atomic +=
long snapshot = Interlocked.Read(ref _big);   // atomic 64-bit read on 32-bit
// Compare-and-swap: the foundation of many lock-free algorithms
Interlocked.CompareExchange(ref _state, newValue, comparand);
```

Для однієї змінної вони значно швидші за блокування й не можуть спричинити deadlock.

### Паралельні колекції

Не обгортайте `Dictionary` власними блокуваннями, коли `System.Collections.Concurrent` має спеціально створений для цього тип:

- `ConcurrentDictionary<K,V>` з атомарними `GetOrAdd` і `AddOrUpdate`;
- `ConcurrentQueue<T>`, `ConcurrentStack<T>`, `ConcurrentBag<T>`;
- `BlockingCollection<T>` для схеми «виробник — consumer» (в асинхронному коді зазвичай краще підходять канали з [Розділу 17](#chapter-17-runtime-internals-and-performance)).

```csharp
var cache = new ConcurrentDictionary<int, User>();
User user = cache.GetOrAdd(id, key => LoadUser(key)); // thread-safe
```

> **Пастка.** Фабрика значення в `GetOrAdd` за конкуренції може виконатися кілька разів, хоча буде збережено лише один результат. Не виконуйте у фабриці дорогих операцій або побічних ефектів, не врахувавши цього.

### `Volatile` і бар’єри пам’яті (концептуально)

Процесор і компілятор *змінюють порядок* операцій із пам’яттю, а ядра зберігають значення в регістрах. Без синхронізації запис одного потоку може стати видимим іншому із затримкою або не в тому порядку:

```csharp
// Thread A
_data = Load();
_ready = true;    // could be reordered/visible before _data on another thread!

// Thread B
if (_ready) Use(_data); // might see _ready == true but stale _data
```

**Бар’єр пам’яті** забороняє переставляти операції через нього й забезпечує їхню видимість; `Volatile.Read`/`Volatile.Write` (і ключове слово `volatile`) вставляють такий бар’єр. Зазвичай цей рівень не потрібен: `lock`, `Interlocked` і паралельні колекції самі встановлюють потрібні бар’єри. Власні алгоритми без блокувань — заняття для фахівців і типове джерело помилок, які проявляються лише під навантаженням у продакшені.

> **Рекомендація.** Віддавайте перевагу синхронізації високого рівня (`lock`, `Interlocked`, паралельні колекції, незмінні дані) перед ручними бар’єрами пам’яті. Використовуйте `volatile`, лише коли розумієте модель пам’яті, і документуйте *причину*.

## Перевірте на практиці

Три програми — по одній для кожної пастки. Перш ніж запускати їх, передбачте виведення: різниця між прогнозом і результатом і є метою цього розділу.

**1. Exception з `async void` завершує процес.**

`verify/path/AsyncVoid/Program.cs` · запустіть із `verify/path` командою `dotnet run --project AsyncVoid`:

```csharp
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
```

```text
async Task: the caller caught ArgumentOutOfRangeException
async void: the call returned normally and the catch below never ran
Unhandled exception. System.ArgumentOutOfRangeException: id ('-1') must be a non-negative value. (Parameter 'id')
Actual value was -1.
   at System.ArgumentOutOfRangeException.ThrowNegative[T](T value, String paramName)
   at System.ArgumentOutOfRangeException.ThrowIfNegative[T](T value, String paramName)
   at Program.<<Main>$>g__Save|0_1(Int32 id) in …/AsyncVoid/Program.cs:line 26
   at System.Threading.Tasks.Task.<>c.<ThrowAsync>b__124_1(Object state)
   at System.Threading.ThreadPoolWorkQueue.Dispatch()
   at System.Threading.PortableThreadPool.WorkerThread.WorkerThreadStart()
   at System.Threading.Thread.StartCallback()
```

У Linux процес завершується з кодом 134; на інших системах код теж буде ненульовим. Зверніть увагу:

- **Exception виникає до першого `await`, але викликувач усе одно не може його перехопити.** Компілятор переносить усе тіло методу в state machine, у її власний `try/catch`, який передає exception будівнику методу. Виклик завершується звичайно.
- **Нижні кадри показують повторне викидання в пулі.** `ThreadPoolWorkQueue.Dispatch` — потік пулу без викликувача, який міг би перехопити exception, тож runtime завершує процес. У вебзастосунку разом із ним завершуються всі запити в роботі.

**2. `await Task.WhenAll` викидає один exception.**

`verify/path/WhenAll/Program.cs` · запустіть із `verify/path` командою `dotnet run --project WhenAll`:

```csharp
// Prove it: await Task.WhenAll rethrows ONE exception; the WhenAll task holds all of them.
Task first = Fail(300, "A (listed first, fails last)");
Task second = Fail(50, "B (listed second, fails first)");
Task all = Task.WhenAll(first, second);

try
{
    await all;
}
catch (Exception e)
{
    Console.WriteLine($"await threw:   {e.GetType().Name}: {e.Message}");
    Console.WriteLine($"all.Exception: {all.Exception!.InnerExceptions.Count} inner exceptions");
    foreach (Exception inner in all.Exception.InnerExceptions)
        Console.WriteLine($"  - {inner.Message}");
}

try { all.Wait(); }                             // the blocking API throws the wrapper instead
catch (AggregateException e) { Console.WriteLine($".Wait() threw: AggregateException with {e.InnerExceptions.Count} inner exceptions"); }

try { await Task.WhenAll(FailTyped(300, "A"), FailTyped(50, "B")); }
catch (Exception e) { Console.WriteLine($"WhenAll over Task<int> threw: {e.Message} (argument order this time)"); }

static async Task Fail(int ms, string name) { await Task.Delay(ms); throw new InvalidOperationException(name); }
static async Task<int> FailTyped(int ms, string name) { await Task.Delay(ms); throw new InvalidOperationException(name); }
```

```text
await threw:   InvalidOperationException: B (listed second, fails first)
all.Exception: 2 inner exceptions
  - B (listed second, fails first)
  - A (listed first, fails last)
.Wait() threw: AggregateException with 2 inner exceptions
WhenAll over Task<int> threw: A (argument order this time)
```

Зверніть увагу:

- **`await` повторно викидає один exception, задача містить обидва.** Це зроблено навмисно: асинхронний код має читатися як синхронний, де виклик викидає один exception. `.Wait()` і `.Result` натомість викидають обгортку `AggregateException`.
- **Неможливо передбачити, який саме exception буде першим.** Для звичайних `Task` це був перший exception за часом; для `Task<int>` — перший у порядку аргументів. Не покладайтеся на жоден варіант: збережіть задачу `WhenAll` у змінній і записуйте `all.Exception?.InnerExceptions`. `Exception` дорівнює `null`, якщо задача скасована, а не завершилася з помилкою; тому `!` у прикладі безпечний лише за наявності помилки.

**3. Синхронне очікування асинхронного коду виснажує пул.** Запустіть програму двічі: з параметром `-- await`, а потім `-- block`.

`verify/path/Starvation/Program.cs` · запустіть із `verify/path` командою `dotnet run --project Starvation -- await`, а потім `-- block`:

```csharp
using System.Diagnostics;

// Prove it: blocking on async work starves the thread pool; awaiting it does not.
// Run twice: `dotnet run -- await` and `dotnet run -- block`.
bool block = args.FirstOrDefault() == "block";
int requests = 50 * Environment.ProcessorCount;
var clock = Stopwatch.StartNew();

Task[] work = Enumerable.Range(0, requests).Select(_ => Task.Run(async () =>
{
    if (block) Task.Delay(1000).Wait();        // sync-over-async: the thread waits for the "I/O"
    else await Task.Delay(1000);               // async: the thread goes back to the pool
})).ToArray();

var probe = Stopwatch.StartNew();
await Task.Run(() => { });                     // one tiny unrelated request, queued behind them
Console.WriteLine($"a tiny unrelated request waited {probe.ElapsedMilliseconds} ms for a thread");

int peakThreads = 0;
while (!work.All(t => t.IsCompleted))
{
    peakThreads = Math.Max(peakThreads, ThreadPool.ThreadCount);
    await Task.Delay(50);
}
Console.WriteLine($"{requests} requests of 1 s each with {(block ? ".Wait()" : "await")}: " +
    $"done in {clock.Elapsed.TotalSeconds:F1} s, peak pool threads {peakThreads}");
```

```text
a tiny unrelated request waited 6 ms for a thread
200 requests of 1 s each with await: done in 1.1 s, peak pool threads 3

a tiny unrelated request waited 10542 ms for a thread
200 requests of 1 s each with .Wait(): done in 11.6 s, peak pool threads 68
```

Прогін виконано на системі з 4 віртуальними процесорами, тому було 200 запитів; кількість ядер вашої машини вплине на число запитів, а тривалість буде іншою. Зверніть увагу:

- **Та сама робота, але вдесятеро повільніше.** За асинхронного очікування 200 односекундних запитів завершуються за 1,1 секунди на 3 потоках пулу: потоки не зайняті під час очікування. У блокувальному варіанті та сама робота триває 11,6 секунди, а пул має зрости до 68 потоків.
- **Збій спричиняє сторонній запит.** Він чекав на потік 10,5 секунди в queue позаду заблокованої роботи. У продакшені сповільнюються всі endpoints, навіть ті, що не блокують потоки.
- **Процесору не було чого робити.** Блокувальний прогін використав 0,46 секунди процесорного часу за 11,7 секунди реального часу на 4 ядрах: потоки чекали, а не працювали. Низьке завантаження процесора, висока затримка всюди й постійне зростання кількості потоків — так виглядає виснаження пулу ззовні, і тому його часто помилково звинувачують у повільній роботі бази даних.
- **Прогін завершується лише тому, що пул повільно додає потоки.** У [Розділ 17](#chapter-17-runtime-internals-and-performance) програма запускається ще раз із лічильниками, щоб показати швидкість додавання потоків і пояснити, чому збільшення мінімального розміру пулу лише відсуває проблему.

Потім виконайте завдання *Знайдіть помилку* нижче: знайдіть усі дефекти в endpoint звіту з `.Result`, `.Wait()` і `Parallel.ForEach` та визначте, який із них спричиняє збій, перш ніж відкривати відповідь.

## Три запитання

**1.** `try/catch` охоплює виклик методу `async void`, який викидає exception у першому рядку, до будь-якого `await`. Чому `catch` не виконується і чому завершується весь процес, а не лише один запит?

<details>
<summary>Відповідь</summary>

- **Тіло методу завжди виконується всередині state machine.** Компілятор переносить його в `MoveNext`, оточений власним `try/catch`, тож там перехоплюється кожен exception, навіть викинутий до першого `await`, і передається будівнику методу. Виклик завершується звичайно.
- **Для `async Task` є місце, куди його зберегти.** Будівник записує exception у повернену `Task`, а викликувач отримує його під час очікування.
- **У `async void` такого місця немає.** Його будівник повторно викидає exception у захопленому `SynchronizationContext`. Якщо контексту немає (ASP.NET Core, фонові служби, консольні застосунки), exception викидається в потоці пулу. Над елементом роботи пулу немає коду, що міг би його перехопити: exception залишається необробленим, і runtime завершує процес разом з усіма його запитами.

Виправлення: повертайте `Task`. Справжню фонову роботу без очікування передавайте в queue або `BackgroundService`, який записує помилки в журнал. Залиште `async void` лише для handlers events і обгорніть усе їхнє тіло в `try/catch`.
</details>

**2.** `await Task.WhenAll(a, b)`, і обидві задачі завершуються з помилкою. Чому `catch` бачить один exception, чому `.Wait()` на тій самій задачі викидає інше і як записати в журнал обидва?

<details>
<summary>Відповідь</summary>

- **Задача `WhenAll` зберігає обидва exceptions** в `AggregateException`.
- **`await` навмисно викидає лише один.** Він повторно викидає перший збережений exception з початковим call stack, щоб асинхронний код читався як синхронний: один виклик — один exception. Який буде «першим», залежить від перевантаження й часу завершення, тому не покладайтеся на порядок.
- **`.Wait()` і `.Result` викидають обгортку:** сам `AggregateException`.

Щоб записати обидва exceptions в журнал, збережіть задачу `WhenAll` у змінній і в блоці `catch` запишіть `task.Exception?.InnerExceptions`. `Exception` дорівнює `null`, якщо задачу скасовано, а не завершено з помилкою.
</details>

**3.** `.Result` в handler запиту проходить усі перевірки й дає збій лише під навантаженням. Який обмежений ресурс він вичерпує і чому це стається лише за високої паралельності?

<details>
<summary>Відповідь</summary>

Потоки пулу. `.Result` утримує потік пулу протягом усього очікування введення-виведення. За низької паралельності є вільні потоки. За високої — зайняті всі потоки, а продовження й callbacks таймерів, які мали б їх звільнити, чекають у queue позаду нових запитів. Пул додає потоки поступово, тому затримка поширюється на всі endpoints, хоча процесор майже не завантажений.

Тест надсилає кілька запитів один за одним, тому потоки пулу не закінчуються. Створення нового `HttpClient` для кожного запиту спричиняє схожу проблему з іншим ресурсом — локальними портами; див. [Keep-Alive, connection pool (пул з’єднань) і вичерпання сокетів](#keep-alive-connection-pooling-and-socket-exhaustion) у Розділ 5.
</details>

## Перевірте на роботі

**Перевірте код.** Пошукайте в сервісі `async void`, `.Result`, `.Wait()` і `.GetAwaiter().GetResult()`. Розподіліть збіги між трьома категоріями: handler events, код запуску або шлях обробки запиту чи повідомлення, який приховує майбутній збій. Потім пройдіть ланцюжком `await` однієї endpoint — від handler до виклику бази даних чи HTTP. Якщо `CancellationToken` передається лише до певного місця, все нижче не можна скасувати.

**Виміряйте.** Переконайтеся, що збираються метрики довжини queue thread pool і кількості потоків: `dotnet.thread_pool.queue.length` та `dotnet.thread_pool.thread.count` в APM для .NET 9+ (перевірте, що графік показує значення, а не швидкість зміни), `ThreadPool Queue Length` і `ThreadPool Thread Count` у `dotnet-counters` (у .NET 9 і 10 із `--counters 'EventCounters\System.Runtime'`; у [Діагностуванні проблеми продуктивності](#diagnosing-a-performance-problem-a-worked-methodology) в Розділ 9 пояснено чому). Без цих метрик виснаження пулу виглядає як повільна база даних. Перегляньте їх за пікового навантаження: queue, що зростає разом із кількістю потоків, — характерна ознака виснаження.

## Вправи

### Знайдіть помилку

Цей handler компілюється, проходить модульний тест і зупиняє сервіс під навантаженням.

```csharp
[HttpGet("/reports/{id:int}")]
public IActionResult GetReport(int id)
{
    var report = _reportService.BuildReportAsync(id).Result;

    var recipients = _db.Subscribers
        .Where(s => s.ReportId == id)
        .ToList();

    Parallel.ForEach(recipients, r =>
    {
        _mailer.SendAsync(r.Email, report).Wait();
    });

    return Ok(report);
}
```

Назвіть усі помітні дефекти, а потім визначте, який спричиняє збій.

<details>
<summary>Відповідь</summary>

Є чотири окремі проблеми, від меншої до найсерйознішої:

1. **`.Result` і `.Wait()` — синхронне очікування асинхронного коду.** Кожен із них блокує потік пулу на весь час очікування введення-виведення.
2. **`Parallel.ForEach` множить блокування в асинхронній роботі.** Він призначений для роботи з обмеженням процесора: кожна ітерація тут блокує потік пулу на `.Wait()` протягом усього надсилання листа, а `Parallel.ForEach` залучає додаткові потоки для додаткових ітерацій. Тому один запит блокує кілька потоків. Для асинхронного розподілення роботи підходить `Parallel.ForEachAsync` із `MaxDegreeOfParallelism`.
3. **`CancellationToken` ніде не використовується.** Якщо клієнт від’єднається, усі листи однаково буде надіслано.
4. **Збій спричиняє виснаження thread pool.** Кожен запит у роботі займає один потік у `.Result` і ще кілька в `.Wait()`; продовження, які мали б звільнити їх, стають у queue позаду нових запитів, а пул додає потоки лише поступово ([Deadlock через синхронне очікування асинхронного коду](#the-sync-over-async-deadlock)). Затримка зростає на *всіх* endpoints, хоча процесор майже не завантажений, тож проблему часто помилково вважають проблемою бази даних.

Виправлення: `async Task<IActionResult>`, лише `await`, `Parallel.ForEachAsync` із `MaxDegreeOfParallelism` і `CancellationToken`, переданий від сигнатури handler до нижчих викликів.

Перевірено в репозиторії (`verify/exercises/Ch08`): на системі з 4 vCPU 20 одночасних запитів (200 мс на введення-виведення звіту, потім чотири листи по 200 мс) тривали **6 с** з цим кодом і **0,8 с** після виправлення. Сторонній запит чекав на потік **1,8 с**, а пул зріс до **49 потоків** замість 5. Друга пара тестів показує, що виправлений варіант зупиняється після переривання запиту, тоді як оригінальний не має токена для скасування.
</details>

### Як би ви вчинили?

Колега додав у PR до нового сервісу ASP.NET Core `ConfigureAwait(false)` після кожного `await`, посилаючись на допис про deadlock. Різниця охоплює 300 рядків у 40 файлах. Що ви напишете в огляді коду?

<details>
<summary>Як міркує досвідчений інженер</summary>

Технічно правильне спостереження: в ASP.NET Core немає `SynchronizationContext`, тож `ConfigureAwait(false)` тут нічого не змінює. Він запобігає deadlock у WinForms, WPF або застарілому ASP.NET. У *бібліотеці*, яку можуть використовувати такі застосунки, це добра практика; у сервісі ASP.NET Core — зайвий шум, що ускладнює читання майбутніх змін.

Але вдала відповідь у коментарі не має на цьому зупинятися. Колега старанно застосував рекомендацію й правильно зрозумів явище, але помилився щодо цього коду. Поясніть механізм (що таке `SynchronizationContext` і що ASP.NET Core його не встановлює), погодьтеся, де ця порада *доречна*, і залиште колезі вибір: прибрати зміни чи зберегти їх для бібліотеки в рішенні.

Зважайте й на масштаб. Якщо команда має правило аналізатора щодо цього, обговорюйте саме правило, а не PR. А якщо решта змін якісна, цілком доречно сказати «зайве, але нешкідливе — не затримуймо через це merge (злиття)»: механічно доданий `ConfigureAwait(false)` шкодить читабельності, а не коректності. У [Розділ 16: Робочі звички розробника рівня middle](#chapter-16-working-like-a-middle-developer) розглянуто різницю між блокувальним коментарем і дрібним зауваженням.
</details>

## Запитання для співбесіди

**Асинхронність і багатопотоковість — у чому різниця?**
Багатопотоковість використовує кілька потоків для паралельної роботи (з обмеженням процесора). Асинхронність дає змогу *не блокувати* потік під час очікування на щось інше (введення-виведення): один потік може обслуговувати багато операцій у роботі. Асинхронність ≠ паралельність: очікування одного виклику через `await` і далі виконується послідовно; паралельність з’являється, коли кілька задач запускають до очікування.

**Тривожний сигнал:** «асинхронність пришвидшує код, бо він виконується паралельно» — один виклик із `await` так само повільний; асинхронність дає масштабованість, а не швидкість.

**`Task` і `ValueTask` — коли потрібен `ValueTask`?**
`Task` — тип посилання, для якого виділяється пам’ять у купі; кожен асинхронний виклик створює такий об’єкт. `ValueTask` уникає цього виділення, коли результат *часто доступний одразу* (влучання в кеш, читання з буфера). Використовуйте його в гарячих API з великою кількістю викликів, більшість яких завершуються синхронно. Не очікуйте `ValueTask` двічі й не зберігайте його — його можна спожити лише один раз. У [Task і ValueTask](#task-vs-valuetask) в Розділ 17 наведено правила.

**Що робить `ConfigureAwait(false)` і де його застосовувати?**
Він указує продовженню не повертатися до захопленого контексту синхронізації, а продовжити роботу в потоці пулу. Використовуйте його в бібліотечному коді, щоб уникати deadlocks і зайвих переходів між контекстами. В ASP.NET Core контексту синхронізації немає, тож там він менш важливий, але для повторно використовуваних бібліотек залишається доброю практикою.

**Чому `.Result` спричиняє deadlock?**
На платформі з однопотоковим контекстом синхронізації (класичний інтерфейс користувача, застарілий ASP.NET) блокування на `.Result`/`.Wait()` займає потік, а продовження після `await` має виконатися *в тому самому потоці*: виникає взаємне очікування. Виправлення — асинхронність до самого низу й відмова від блокування на асинхронному коді. В ASP.NET Core контексту синхронізації немає, тому продовження можуть виконуватися в будь-якому потоці пулу й такого deadlock не буде; однак синхронне очікування й далі займає по потоку пулу на запит і виснажує пул під навантаженням.

**Тривожний сигнал:** «обгорніть виклик у `Task.Run(...).Result`, і буде безпечно» — це лише марнує ще один потік; потрібно зробити весь ланцюжок асинхронним.

**Для чого потрібен `CancellationToken`?**
Для кооперативного скасування. Передавайте токен через асинхронні виклики; викликувач може запросити скасування (тайм-аут, дія користувача, перерваний запит), а коректно написані методи перевіряють `IsCancellationRequested` або передають токен далі, викидаючи `OperationCanceledException`. Завжди передавайте токен аж до викликів бази даних і HTTP, щоб робота справді зупинилася.

**Тривожний сигнал:** «скасування токена негайно зупиняє операцію» — скасування кооперативне; нічого не зупиниться, доки код не перевірить токен.

**Як зробити клас потокобезпечним?**
Приблизно в порядку переваги: зробіть його незмінним (немає спільного змінюваного стану — нічого захищати); обмежте зміни одним потоком; використовуйте паралельні колекції (`ConcurrentDictionary`) або захистіть спільний стан через `lock`. Залишайте заблоковані ділянки короткими, ніколи не використовуйте `await` усередині `lock` і завжди блокуйте окремий приватний об’єкт.

**`lock` чи `Interlocked`?**
`lock` (`Monitor`) забезпечує взаємне виключення на ділянці коду — використовуйте його для інваріантів із кількох кроків. `Interlocked` виконує одну атомарну операцію (збільшення, порівняння й заміну) без блокування, що значно дешевше для окремого лічильника чи прапорця. Використовуйте `Interlocked` для захисту однієї змінної, а `lock` — для інваріанта, що охоплює кілька змінних.

**Для чого потрібен `IAsyncEnumerable<T>`?**
Для асинхронного потокового читання: можна застосувати `await foreach` до елементів, які надходять із затримкою (сторінки API, рядки запиту до бази), не буферизуючи весь набір у пам’яті. Це поєднання відкладеного перебору з керуванням consumer й асинхронного введення-виведення дає змогу почати обробку перших елементів до появи останніх.

> **Уточнення:** *Потрібно виконати 100 незалежних HTTP-викликів. Що робити?* Запустіть їх усі (`Select(x => CallAsync(x))`) і дочекайтеся через `await Task.WhenAll`; бажано обмежити паралельність за допомогою `SemaphoreSlim`, щоб не вичерпати сокети й не перевантажити залежний сервіс.
