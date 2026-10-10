# Розділ 10: Основи проєктування

Досвідчений розробник — це не той, хто завчив двадцять три шаблони з книжки. Це той, хто дивиться на клубок коду й *відчуває*, де мають проходити межі, обирає шаблон так, як тесля обирає потрібне долото, і — що особливо важливо — знає, коли залишити долото в коробці й просто забити цвях. Цей розділ присвячено такому судженню: що підштовхує до певного проєктного рішення, якою є його ціна та коли лікування стає гіршим за хворобу.

Після цього ви зможете пояснювати кожен принцип SOLID через те, що ламається без нього; обирати між звичайним `if`, Strategy і Decorator (зокрема розуміти, коли правильна відповідь — жоден шаблон); називати запах коду й рефакторинг, що його усуває; а також розміщувати код у шарах так, щоб бізнес-правила не залежали від бази даних. Кожне наведене тут правило — ставка на те, де з’явиться наступна зміна: воно збирає код, який змінюється з однієї причини, в одному місці за чіткою межею, а непрямий рівень окупається лише тоді, коли ця зміна справді настає.

Спершу розглянемо принципи, адже на них спирається все інше. Далі будуть класичні шаблони як втілення цих принципів у коді, а потім — прикладні шаблони, з якими ви щодня стикаєтеся в .NET. Чистий код і його запахи застосують ті самі сили до окремої назви чи методу, а завершальні розділи поширять їх на ціле рішення: багатошарову архітектуру й чисту архітектуру. Детальніше про DDD, CQRS, вертикальні зрізи, моноліти й мікросервіси читайте в [Розділі 21: Архітектура](#chapter-21-architecture).

## Принципи: основа шаблонів

Шаблони — це конкретні прийоми, а принципи — стратегія, що підказує, який прийом обрати. Якщо засвоїти принципи, більшість шаблонів стають очевидними, і, що не менш важливо, ви розумієте, коли *не* варто до них звертатися.

### SOLID

П’ять принципів, які разом спрямовують до коду, що легко змінювати.

**S — принцип єдиної відповідальності (Single Responsibility Principle).** Клас має мати лише одну причину для зміни. Інакше кажучи, він має відповідати одному зацікавленому учаснику чи одному аспекту.

```csharp
// BEFORE: this class has three reasons to change — report format,
// business rules, and delivery mechanism all live together.
public class InvoiceService
{
    public decimal CalculateTotal(Invoice inv) { /* business rules */ return 0; }
    public string RenderPdf(Invoice inv) { /* formatting */ return ""; }
    public void SendEmail(Invoice inv) { /* delivery */ }
}

// AFTER: each concern is separable and independently testable.
public class InvoiceCalculator { public decimal CalculateTotal(Invoice inv) => 0; }
public class InvoicePdfRenderer { public string Render(Invoice inv) => ""; }
public class InvoiceEmailSender { public void Send(Invoice inv) { } }
public record Invoice;
```

Коли змінюється бібліотека PDF, змінюється лише засіб формування документа. Коли змінюються податкові правила — лише калькулятор. Уся суть саме в такій ізоляції.

**O — принцип відкритості/закритості (Open/Closed Principle).** Програмне забезпечення має бути відкритим до розширення, але закритим для модифікації: додавайте поведінку новим кодом, а не редагуванням наявного перевіреного коду.

```csharp
// BEFORE: every new shipping method edits this switch. Closed for extension.
public decimal Cost(string method, Order o) => method switch
{
    "standard" => 5m,
    "express"  => 15m,
    _ => throw new ArgumentException()
};

// AFTER: the Strategy pattern (below). A new method = a new class.
// The calculator is never touched again.
```

Саме через принцип відкритості/закритості існують Strategy, Decorator і Factory. Якщо втретє додаєте варіант у `switch`, це принцип підказує, що час зробити рефакторинг.

**L — принцип підстановки Лісков (Liskov Substitution Principle).** Підтипи мають використовуватися всюди, де очікується їхній базовий тип, не дивуючи викликачів. Похідний клас повинен дотримуватися контракту базового класу.

```csharp
// VIOLATION: Square "is a" Rectangle mathematically, but overriding the
// setters to keep sides equal breaks code that sets width and height
// independently and expects them to stay independent.
public class Rectangle { public virtual int Width { get; set; } public virtual int Height { get; set; } }
public class Square : Rectangle
{
    public override int Width { set { base.Width = base.Height = value; } }
    public override int Height { set { base.Width = base.Height = value; } }
}
// A test expecting (w=5, set h=4 => area 20) suddenly gets 16. The subtype lied.
```

Зазвичай потрібно переосмислити ієрархію (використати композицію чи спільний інтерфейс `IShape`), а не нав’язувати зв’язок «є різновидом», який не відповідає поведінці. LSP застерігає від зловживання успадкуванням заради повторного використання коду.

**I — принцип розділення інтерфейсів (Interface Segregation Principle).** Не змушуйте клієнтів залежати від методів, якими вони не користуються. Віддавайте перевагу багатьом малим цільовим інтерфейсам замість одного перевантаженого.

```csharp
// BEFORE: a printer that only prints is forced to implement scanning and faxing.
public interface IMachine { void Print(); void Scan(); void Fax(); }

// AFTER: split by capability; a class implements only what it truly does.
public interface IPrinter { void Print(); }
public interface IScanner { void Scan(); }
// A simple printer implements IPrinter alone; a multifunction device implements both.
```

Занадто широкі інтерфейси поширюють зв’язаність: зміна `Fax()` змушує перекомпілювати й повторно тестувати кожну реалізацію, навіть ту, де метод замінено на `throw new NotImplementedException()`. Це вже готове порушення LSP.

**D — принцип інверсії залежностей (Dependency Inversion Principle).** Модулі високого рівня мають залежати від абстракцій, а не від конкретних деталей низького рівня; і ті, й інші мають залежати від абстракцій.

```csharp
// BEFORE: the high-level order logic is welded to a concrete SMTP class.
public class OrderProcessor
{
    private readonly SmtpEmailSender _sender = new();  // hard dependency
}

// AFTER: depend on an abstraction, injected in. The concrete type is chosen
// at composition time, and tests substitute a fake freely.
public class OrderProcessor
{
    private readonly IEmailSender _sender;
    public OrderProcessor(IEmailSender sender) => _sender = sender;
}
public interface IEmailSender { void Send(string to, string body); }
```

Інверсія залежностей лежить в основі всієї системи .NET [dependency injection (впровадження залежностей)](#dependency-injection) (Розділ 3). Кожен конструктор, що приймає інтерфейс замість створеного через `new` конкретного класу, застосовує цей принцип. Саме він дає змогу практично використовувати решту принципів.

### Решта інструментарію

- **DRY (Don't Repeat Yourself — не повторюйся):** кожне *знання* має мати одне авторитетне представлення. Зверніть увагу: йдеться про «знання», а не «текст». Два схожі методи, які змінюються з різних причин, *не* порушують принцип, а їх об’єднання створює хибну зв’язаність. Надмірне прагнення до DRY — справжня помилка досвідченого розробника: іноді невелике дублювання дешевше за невдалу абстракцію.
- **KISS (Keep It Simple, Stupid — не ускладнюй):** віддавайте перевагу найпростішому працездатному рішенню. Дотепний однорядковий вираз, яким ви пишаєтеся, може стати тягарем для наступного читача.
- **YAGNI (You Aren't Gonna Need It — це вам не знадобиться):** не створюйте рішення для уявних майбутніх вимог. Абстракція, додана «про всяк випадок», зазвичай виявляється неправильною, коли потреба нарешті виникає, а до того часу лише створює витрати. Цей принцип стримує надмірне використання шаблонів.
- **Розділення відповідальностей:** різні аспекти системи — інтерфейс користувача, бізнес-логіка, доступ до даних — належать до різних модулів. Багатошарова й чиста архітектури наприкінці розділу масштабують цей принцип на всю систему.
- **Закон Деметри (принцип найменшої обізнаності):** метод має взаємодіяти лише з безпосередніми співпрацівниками, а не проходити крізь них. `order.Customer.Address.Country.Code` — «катастрофа потяга», що зв’язує вас з усім графом об’єктів; натомість попросіть найближчий об’єкт надати потрібне. (Ланцюжки LINQ — свідомий exception (виняток): вони працюють в одному pipeline (конвеєр), а не з мережею окремих об’єктів.)
- **Композиція замість успадкування:** віддавайте перевагу збиранню поведінки з малих впроваджених частин замість глибоких ієрархій успадкування. Успадкування жорстке: воно визначається під час компіляції, створює проблеми крихкого базового класу й нав’язує дочірньому класу весь контракт батьківського. Композиція (основа Strategy, Decorator і DI) гнучка й тестована. Якщо збираєтеся написати `class X : Y` заради повторного використання коду, а не справжнього відношення «є різновидом», зупиніться й подумайте, чи має X натомість *містити* Y.

## Що насправді являє собою design pattern

Design pattern (шаблон проєктування) — це іменоване багаторазове рішення типової проблеми проєктування.

Шаблон — це *зафіксований компроміс, який повторювали достатньо часто, щоб дати йому назву*. Коли ви кажете «шаблон Strategy», ви описуєте не ієрархію класів, а рішення обміняти невелику непрямість на можливість змінювати алгоритм під час виконання. Ієрархія класів — лише форма, яку це рішення набуває в коді.

Таке переосмислення важливе, бо підказує, як вивчати шаблони. Не заучуйте UML. Запам’ятовуйте *проблему*, яку розв’язує кожен шаблон, і *ціну*, яку він має. Тоді, зіткнувшись із цією проблемою, ви самі згадаєте потрібний шаблон.

Шаблони також дають командам спільну термінологію. Коли колега каже «обгорнімо репозиторій декоратором, щоб додати кешування», вісім слів передають ціле проєктне рішення. Така стислість має реальну цінність — тому шаблони варто вивчати навіть розробникам, які могли б самостійно вигадати ці рішення.

### Небезпека надмірного використання шаблонів

Ось неприємна істина, що відрізняє розробника середнього рівня від досвідченого: **більшості коду шаблон не потрібен, а передчасне його застосування шкодить.**

У спільноті таку проблему називають «шаблонітом» або «архітектурною космонавтикою». Це виглядає так: розробник вивчає шаблони, захоплюється ними й починає бачити їх усюди. Простий `if/else` перетворюється на Strategy із трьома класами й фабрикою. У служби з двома методами з’являються інтерфейс, абстрактний базовий клас і декоратор «для гнучкості». За пів року, щоб відстежити один запит, доводиться відкрити одинадцять файлів, і ніхто вже не знає, де виконується справжня робота.

> **Застереження щодо надмірного використання:** кожен шаблон додає непрямий рівень, а за нього платить кожен майбутній читач коду. Шаблон виправданий, лише якщо надана ним гнучкість вам справді знадобиться. Гіпотетична гнучкість — «колись, можливо, доведеться змінити базу даних» — зазвичай є невдалим компромісом. Це YAGNI (You Aren't Gonna Need It — це вам не знадобиться), найважливіший принцип цього розділу.

Правильна модель мислення така: шаблони відповідають на *вже відчутний біль*, а не страхують від уявного. Спершу напишіть простий варіант. Коли він почне заважати — наприклад, доведеться змінювати той самий `switch` у п’яти місцях або в класу з’являться три не пов’язані між собою причини для змін, — *тоді* зробіть рефакторинг до шаблону, який полегшить саме цю проблему. Тому найкраще вивчати шаблони разом із рефакторингом: шаблони — це пункти призначення, а рефакторинг — шлях до них.

## Породжувальні шаблони

Породжувальні шаблони описують *як створюються об’єкти*. Спільна риса — вони відокремлюють код, який використовує об’єкт, від коду, що визначає, який саме об’єкт створити та як його зв’язати з іншими.

### Factory Method

Шаблон Factory Method визначає метод, завдання якого — створити об’єкт, відкладаючи вибір конкретного типу до підкласу чи налаштувань. Код-викликач залежить лише від абстракції.

Він розв’язує таку проблему: коду потрібен об’єкт, але конкретний тип залежить від контексту, і ви не хочете розкидати `new SomeConcreteClass()` серед бізнес-логіки. Безпосередній виклик `new` приварює код до конкретної реалізації; фабричний метод утворює межу.

```csharp
public interface INotification
{
    Task SendAsync(string recipient, string message);
}

public sealed class EmailNotification : INotification
{
    public Task SendAsync(string recipient, string message) =>
        Console.Out.WriteLineAsync($"Email to {recipient}: {message}");
}

public sealed class SmsNotification : INotification
{
    public Task SendAsync(string recipient, string message) =>
        Console.Out.WriteLineAsync($"SMS to {recipient}: {message}");
}

// The factory method encapsulates the choice.
public static class NotificationFactory
{
    public static INotification Create(NotificationChannel channel) => channel switch
    {
        NotificationChannel.Email => new EmailNotification(),
        NotificationChannel.Sms   => new SmsNotification(),
        _ => throw new ArgumentOutOfRangeException(nameof(channel))
    };
}

public enum NotificationChannel { Email, Sms }
```

Викликач пише `NotificationFactory.Create(channel)` і отримує `INotification`. Він не знає про існування `SmsNotification`. Якщо згодом додати `PushNotification`, зміниться лише одне місце.

> **Рекомендація для .NET:** у застосунку з dependency injection рідко доводиться власноруч писати такі статичні фабрики. Контейнер DI *і є* вашою фабрикою. Щоб вибирати під час виконання одну із зареєстрованих служб, впровадьте `IEnumerable<INotification>` або використовуйте іменовані служби, додані в .NET 8 (`services.AddKeyedScoped<INotification, EmailNotification>("email")`). Власноручні фабрики виправдані, коли логіка створення справді складна або міститься в бібліотеці, яка не має залежати від контейнера.

### Abstract Factory (коротко)

Якщо Factory Method створює один продукт, то Abstract Factory — *сімейства* пов’язаних продуктів, які мають використовуватися разом. Класичний приклад — кросплатформний набір UI-компонентів: `WindowsWidgetFactory` створює `WindowsButton` і `WindowsCheckbox`, а `MacWidgetFactory` — відповідники для Mac. Ви не зможете випадково поєднати кнопку Windows із прапорцем Mac, бо одна фабрика надає цілий узгоджений набір. На практиці цей шаблон громіздкий; він значно частіше трапляється в коді платформ (реальний приклад — `DbProviderFactory` з ADO.NET), ніж у коді, який ви пишете самі.

### Builder

Шаблон Builder відокремлює *створення* складного об’єкта від його *представлення* й дає змогу складати об’єкт крок за кроком. Він корисний, коли в об’єкта багато необов’язкових параметрів, важливий порядок створення чи перевірка або потрібен незмінний результат, сформований зрозумілим плавним ланцюжком викликів.

Він усуває проблему «телескопічного конструктора»: конструктора з вісьмома параметрами, половина яких необов’язкова, коли викликачі пишуть `new Report(null, null, true, null, false, ...)`, а значення аргументів нікому не зрозумілі.

```csharp
public sealed class EmailMessage
{
    public string From { get; }
    public IReadOnlyList<string> To { get; }
    public string Subject { get; }
    public string Body { get; }
    public bool IsHtml { get; }
    public IReadOnlyList<string> Attachments { get; }

    private EmailMessage(string from, List<string> to, string subject,
                         string body, bool isHtml, List<string> attachments)
    {
        From = from; To = to; Subject = subject;
        Body = body; IsHtml = isHtml; Attachments = attachments;
    }

    public sealed class Builder
    {
        private string _from = "";
        private readonly List<string> _to = new();
        private string _subject = "";
        private string _body = "";
        private bool _isHtml;
        private readonly List<string> _attachments = new();

        public Builder From(string address) { _from = address; return this; }
        public Builder AddRecipient(string address) { _to.Add(address); return this; }
        public Builder WithSubject(string subject) { _subject = subject; return this; }
        public Builder WithHtmlBody(string html) { _body = html; _isHtml = true; return this; }
        public Builder WithTextBody(string text) { _body = text; _isHtml = false; return this; }
        public Builder Attach(string path) { _attachments.Add(path); return this; }

        public EmailMessage Build()
        {
            if (_from.Length == 0) throw new InvalidOperationException("Sender is required.");
            if (_to.Count == 0) throw new InvalidOperationException("At least one recipient is required.");
            return new EmailMessage(_from, _to, _subject, _body, _isHtml, _attachments);
        }
    }
}

// Usage reads like a sentence:
var email = new EmailMessage.Builder()
    .From("noreply@shop.com")
    .AddRecipient("customer@example.com")
    .WithSubject("Your order shipped")
    .WithHtmlBody("<h1>On its way!</h1>")
    .Attach("invoice.pdf")
    .Build();
```

Кожен метод повертає `this`, утворюючи плавний ланцюжок. `Build()` — єдина точка, де перевіряються інваріанти, тож `EmailMessage` не може існувати в некоректному стані.

> **Примітка про сучасний C#:** у простих випадках C# часто безкоштовно надає переваги будівника. Ініціалізатори об’єктів із властивостями `required` та `init` (`new EmailMessage { From = "...", To = [...] }`) дають змогу створювати об’єкти читабельно й із необов’язковими значеннями без окремого класу будівника. Записи з виразами `with` забезпечують незмінні копії. Використовуйте повний Builder, лише коли створення містить справжню логіку — умовні кроки, накопичення даних, поетапну перевірку, — а не просто встановлення властивостей. До речі, ви постійно використовуєте будівники: `WebApplication.CreateBuilder(args)` і `StringBuilder` — саме цей шаблон.

### Singleton

Шаблон Singleton гарантує, що клас матиме рівно один екземпляр, і надає глобальну точку доступу до нього. Це найвідоміший шаблон і той, який у сучасному .NET майже ніколи не варто реалізовувати власноруч.

Ось класична потокобезпечна форма з `Lazy<T>`, щоб ви її впізнали:

```csharp
public sealed class ConfigurationCache
{
    private static readonly Lazy<ConfigurationCache> _instance =
        new(() => new ConfigurationCache());

    public static ConfigurationCache Instance => _instance.Value;

    private ConfigurationCache() { /* expensive one-time load */ }

    public string? Get(string key) => /* ... */ null;
}
```

`Lazy<T>` безкоштовно забезпечує потокобезпечну відкладену ініціалізацію, тож вам не доводиться ризикувати з тонкими помилками подвійного блокування. Приватний конструктор забороняє викликати `new` ззовні.

А ось чому цей шаблон має погану репутацію:

> **Пастки Singleton:**
> - **Це прихований глобальний змінний стан.** Будь-який код може звернутися до `ConfigurationCache.Instance` і змінити його. Це невидима зв’язаність: залежність відсутня в сигнатурі конструктора чи методу.
> - **Він руйнує тестованість.** У модульному тесті не можна підставити фальшивку, бо залежність жорстко прив’язана до статичної властивості, а не впроваджена. Спільний екземпляр також дає тестам змогу витікати станом один в одного.
> - **Час життя прив’язаний до процесу, а не області.** У вебзастосунку часто потрібен «один екземпляр на запит», чого статичний singleton не виражає.

> **Рекомендація: віддавайте перевагу часу життя під керуванням DI, а не шаблону Singleton.** Зареєструйте тип з singleton-*часом життя* (див. [Часи життя служб](#service-lifetimes) у Розділі 3) і доручіть контейнеру його надавати: `services.AddSingleton<IConfigurationCache, ConfigurationCache>()`. Ви отримуєте гарантію одного екземпляра, але залежність стає явною в конструкторах, доступною для мокування й позбавленою глобального статичного доступу. Вам був потрібен *час життя*, а *глобальна точка доступу* ніколи не була перевагою — це був тягар. Власноручний Singleton залиште для рідкісних випадків, коли контейнера немає.

### Prototype (коротко)

Шаблон Prototype створює нові об’єкти шляхом *клонування* наявного екземпляра замість створення з нуля. Це корисно, коли створення дороге або потрібна копія налаштованого об’єкта. У C# це відповідає конструкторам копіювання, `ICloneable` (краще уникати: контракт поверхневого й глибокого копіювання неоднозначний) або, найбільш ідіоматично, [записам із виразами `with`](#records-value-equality-and-with-expressions) (Розділ 1): `var modified = original with { Status = "Revised" };` створює поверхневу копію зі зміненою властивістю. Ця мовна можливість зробила окремий шаблон Prototype майже непомітним у сучасному C#.

## Структурні шаблони

Структурні шаблони присвячені *композиції*: тому, як збирати об’єкти й класи у більші структури, зберігаючи їх гнучкість.

### Adapter

Шаблон Adapter обгортає несумісний інтерфейс у той, якого очікує ваш код, і дає змогу взаємодіяти класам, які інакше не могли б співпрацювати. Це електричний перехідник у світі програмного забезпечення: прилад і розетка справні, але їхні роз’єми не збігаються, тож між ними вставляють невеликий адаптер.

Adapter постійно стає в пригоді під час інтеграції сторонніх бібліотек чи застарілого коду. Застосунок визначає інтерфейс, якого *потребує*, а адаптер перетворює його на інтерфейс, який *надає* зовнішній код.

```csharp
// What our application wants to depend on.
public interface IPaymentGateway
{
    Task<bool> ChargeAsync(decimal amount, string currency, string cardToken);
}

// A third-party SDK we don't control — awkward, differently named API.
public sealed class StripeSdkClient
{
    public Task<StripeChargeResult> CreateChargeAsync(long amountInCents, string curr, string source)
        => Task.FromResult(new StripeChargeResult { Succeeded = true });
}
public sealed class StripeChargeResult { public bool Succeeded { get; set; } }

// The adapter: speaks our language on the outside, Stripe's on the inside.
public sealed class StripePaymentAdapter : IPaymentGateway
{
    private readonly StripeSdkClient _stripe;
    public StripePaymentAdapter(StripeSdkClient stripe) => _stripe = stripe;

    public async Task<bool> ChargeAsync(decimal amount, string currency, string cardToken)
    {
        long cents = (long)(amount * 100);                    // translate units
        var result = await _stripe.CreateChargeAsync(cents, currency, cardToken);
        return result.Succeeded;                              // translate the result shape
    }
}
```

Код предметної області залежить від `IPaymentGateway` і ніколи не бачить Stripe. Щоб перейти на PayPal, напишіть `PayPalPaymentAdapter` — бізнес-логіка не зміниться. Адаптер також стає єдиним місцем для перетворення одиниць вимірювання й обробки особливостей API, ізолюючи цей безлад.

### Decorator

Шаблон Decorator додає об’єкту нову поведінку, обгортаючи його іншим об’єктом із тим самим інтерфейсом. Оскільки обгортка реалізує той самий інтерфейс, викликачі не помічають різниці. Декоратори можна накладати один на одного, пошарово додаючи поведінку; кожен бере на себе частину відповідальності.

Це один із найцінніших шаблонів у світі .NET, і він безпосередньо відповідає на запитання, яке виникає у вашій роботі: «Як додати до служби кешування, logging (журналювання) чи retries (повторні спроби), не змінюючи саму службу?» Це пряме застосування принципу відкритості/закритості.

```csharp
public interface IProductRepository
{
    Task<Product?> GetByIdAsync(int id);
}

public sealed class SqlProductRepository : IProductRepository
{
    public async Task<Product?> GetByIdAsync(int id)
    {
        // hit the database
        await Task.Delay(50);
        return new Product(id, "Widget");
    }
}

// A decorator: same interface, wraps another instance, adds caching.
public sealed class CachingProductRepository : IProductRepository
{
    private readonly IProductRepository _inner;
    private readonly IMemoryCache _cache;

    public CachingProductRepository(IProductRepository inner, IMemoryCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public async Task<Product?> GetByIdAsync(int id)
    {
        if (_cache.TryGetValue(id, out Product? cached))
            return cached;

        var product = await _inner.GetByIdAsync(id);
        if (product is not null)
            _cache.Set(id, product, TimeSpan.FromMinutes(5));
        return product;
    }
}

public record Product(int Id, string Name);
```

`CachingProductRepository` *містить* `IProductRepository` і водночас *є* `IProductRepository`. Він додає кешування й передає справжню роботу внутрішній реалізації. Його можна обгорнути `LoggingProductRepository`, а потім `RetryingProductRepository`, складаючи поведінку, наче шари цибулі. Кожен клас має рівно одну причину для зміни.

> **Зв’язок з ASP.NET Core:** [pipeline проміжного ПЗ](#the-middleware-pipeline-request-lifecycle) (Розділ 5) — це шаблон Decorator (поєднаний із Chain of Responsibility), що працює з HTTP-запитом. Кожен компонент обгортає наступний і за потреби виконує роботу до та після виклику `await _next(context)`. Створюючи проміжне ПЗ автентифікації, logging чи exception handling (обробка винятків), ви декоруєте pipeline запитів.

> **Декорування DI:** вбудований контейнер не має повноцінної підтримки реєстрації декораторів, тому бібліотека **Scrutor** стала майже повсюдною: `services.Decorate<IProductRepository, CachingProductRepository>()`. Вона реєструє декоратор, і контейнер автоматично впроваджує внутрішню реалізацію. Це ідіоматичний спосіб додавати до служби наскрізні аспекти .NET, не змінюючи саму службу.

### Решта коротко

- **Facade** надає єдиний спрощений інтерфейс до складної підсистеми. Якщо ви створюєте `OrderService` із методом `PlaceOrder`, який усередині координує складські запаси, оплату й доставку, така служба є фасадом. Він зменшує обсяг знань, потрібний викликачам.
- **Proxy** надає об’єкт-замісник, що контролює доступ до іншого об’єкта — для lazy loading (відкладене завантаження), контролю доступу чи віддаленого виклику. Проксі lazy loading EF Core і типізовані клієнти на основі `HttpClient`, які звертаються до віддалених служб, — приклади Proxy. Структурно він схожий на Decorator, але *мета* інша: Proxy контролює доступ до того самого концептуального об’єкта, а Decorator додає йому поведінку.
- **Composite** дає змогу однаково працювати з окремими об’єктами та їхніми групами через спільний інтерфейс — наприклад, у дереві файлової системи і папка, і файл мають метод `GetSize()`. Використовуйте його для рекурсивних ієрархій «частина — ціле».
- **Bridge** відокремлює абстракцію від реалізації, щоб їх можна було змінювати незалежно й уникнути комбінаторного вибуху підкласів. Він рідко потрібен у коді застосунку; згадайте про нього, якщо вам інакше знадобляться `RedButton`, `BlueButton`, `RedCheckbox`, `BlueCheckbox` тощо, і потрібно розділити «форму» та «колір».
- **Flyweight** спільно використовує незмінний стан багатьох об’єктів, щоб заощадити пам’ять за великої кількості схожих екземплярів. Інтернування рядків у .NET — приклад Flyweight. Ви навряд чи реалізовуватимете його поза сценаріями з критичною продуктивністю, наприклад рендерингом чи рушіями ігор.

## Поведінкові шаблони

Поведінкові шаблони описують, *як об’єкти взаємодіють і як розподіляється відповідальність* — алгоритми й потоки керування між об’єктами, що співпрацюють.

### Strategy

Шаблон Strategy визначає сімейство взаємозамінних алгоритмів за спільним інтерфейсом і дає змогу замінювати їх під час виконання. Це протиотрута від розрослого `switch`, до якого постійно додають нові варіанти.

Проблема така: одну операцію можна виконати кількома способами (обчислити доставку, стиснути файл, ранжувати результати пошуку), вибір способу змінюється, а вплітати цей вибір у величезну умову, яку доведеться редагувати всім, не хочеться.

```csharp
public interface IShippingStrategy
{
    decimal CalculateCost(Order order);
}

public sealed class StandardShipping : IShippingStrategy
{
    public decimal CalculateCost(Order order) => 5.00m + order.Weight * 0.50m;
}

public sealed class ExpressShipping : IShippingStrategy
{
    public decimal CalculateCost(Order order) => 15.00m + order.Weight * 1.20m;
}

public sealed class FreeShipping : IShippingStrategy
{
    public decimal CalculateCost(Order order) => 0m;
}

public sealed class ShippingCalculator
{
    private readonly IShippingStrategy _strategy;
    public ShippingCalculator(IShippingStrategy strategy) => _strategy = strategy;
    public decimal Calculate(Order order) => _strategy.CalculateCost(order);
}

public record Order(decimal Weight);
```

Щоб додати «доставку наступного дня», достатньо написати новий клас — наявні стратегії й калькулятор не змінюються (знову принцип відкритості/закритості). Зауважте, що стратегія з одним методом фактично є функцією; у C# часто можна передати `Func<Order, decimal>` замість визначення інтерфейсу. Використовуйте інтерфейс, якщо стратегія має стан, кілька методів або потребує реєстрації в DI; делегат — якщо це справді одна функція.

### Observer

Шаблон Observer дає змогу об’єкту (суб’єкту) автоматично сповіщати список залежних об’єктів (спостерігачів) про зміну стану, не знаючи, хто вони. Це видавець-підписник на рівні об’єктів.

У C# майже ніколи не доводиться реалізовувати Observer вручну, адже мова й платформа надають три вбудовані його форми:

1. **Events (події) й делегати** — класичний механізм `event EventHandler` є Observer, вбудованим у мову. `button.Click += OnClick` підписує спостерігача.
2. **`IObservable<T>` / `IObserver<T>`** — реактивні інтерфейси BCL, основа Reactive Extensions (Rx.NET) для компонування асинхронних потоків events операторами в стилі LINQ.
3. **Шини повідомлень і events** — `INotificationHandler` у MediatR або диспетчер доменних events є Observer на рівні застосунку.

```csharp
public sealed class StockTicker
{
    // The 'event' keyword IS the Observer pattern in C#.
    public event Action<string, decimal>? PriceChanged;

    public void UpdatePrice(string symbol, decimal price)
        => PriceChanged?.Invoke(symbol, price);   // notify all observers
}

// Observers subscribe without the ticker knowing anything about them.
var ticker = new StockTicker();
ticker.PriceChanged += (sym, price) => Console.WriteLine($"Logger: {sym} = {price}");
ticker.PriceChanged += (sym, price) => { /* update a dashboard */ };
ticker.UpdatePrice("MSFT", 425.30m);
```

> **Пастка:** суб’єкт утримує кожного підписника, доки той не відпишеться. Це класичний витік через events .NET, розглянутий у [«Events: делегати із запобіжниками»](#events-delegates-with-guardrails) (Розділ 1). `IObservable<T>` вбудовує виправлення в API: підписка повертає `IDisposable`, який треба звільнити, щоб відписатися.

### Mediator

Шаблон Mediator додає об’єкт, що приховує взаємодію між групою об’єктів, щоб вони більше не посилалися один на одного безпосередньо, а спілкувалися *через* посередника. Заплутана мережа зв’язків «багато до багатьох» перетворюється на впорядковану структуру «центр і вузли».

У .NET цей шаблон асоціюється з бібліотекою **MediatR**, яку більшість команд використовує для обробки запитів у стилі CQRS (сам CQRS розглянуто в [Розділі 21](#chapter-21-architecture)). Замість п’яти залежностей від служб контролер залежить лише від `IMediator` і надсилає запит; MediatR спрямовує його до єдиного handler (обробник), який знає, як його виконати.

```csharp
// A request (the message) — carries data, knows nothing about its handler.
public record GetCustomerByIdQuery(int CustomerId) : IRequest<CustomerDto>;

// The handler — the only thing that knows how to satisfy this request.
public sealed class GetCustomerByIdHandler : IRequestHandler<GetCustomerByIdQuery, CustomerDto>
{
    private readonly ICustomerRepository _repo;
    public GetCustomerByIdHandler(ICustomerRepository repo) => _repo = repo;

    public async Task<CustomerDto> Handle(GetCustomerByIdQuery request, CancellationToken ct)
    {
        var customer = await _repo.GetByIdAsync(request.CustomerId);
        return new CustomerDto(customer.Id, customer.Name);
    }
}

// The controller depends on ONE thing, the mediator.
[ApiController, Route("customers")]
public sealed class CustomersController : ControllerBase
{
    private readonly IMediator _mediator;
    public CustomersController(IMediator mediator) => _mediator = mediator;

    [HttpGet("{id}")]
    public async Task<CustomerDto> Get(int id) =>
        await _mediator.Send(new GetCustomerByIdQuery(id));
}

public record CustomerDto(int Id, string Name);
```

Контролер і handler повністю розв’язані — жоден не посилається на тип іншого. Поведінки pipeline MediatR також дають змогу додавати до кожного запиту наскрізні аспекти (перевірку, logging, транзакції). Це Chain of Responsibility, нашарований на Mediator.

> **Застереження щодо надмірного використання:** MediatR настільки популярний, що його застосовують за інерцією. Для CRUD-застосунку з тонкими контролерами routing (маршрутизація) всіх запитів через посередника в пам’яті може додати формальностей — окремий клас запиту й handler для кожної операції, — але не забезпечити потрібного вам розв’язання. Використовуйте його, коли непрямий рівень окупається: багато handlers, наскрізні поведінки pipeline або справжня потреба не давати транспортному рівню (контролерам) знати про рівень застосунку. Службі з трьома endpoints (кінцеві точки) він не потрібен.

> **Про ліцензування:** у квітні 2025 року супровідник MediatR Джиммі Богард оголосив, що MediatR і AutoMapper переходять на комерційну ліцензію для фінансування підтримки. Наявні версії залишаються під відкритими ліцензіями, але нові основні версії будуть комерційними. Практичний наслідок: перед звичним «просто додаймо MediatR» тепер варто перевірити ліцензію, а також замислитися, чи потрібна бібліотека взагалі. Власні інтерфейси handlers разом із DI охоплюють більшість сценаріїв MediatR, а безкоштовна бібліотека **Wolverine** (MIT-ліцензія починаючи з v4) є повноціннішою альтернативою; для зіставлення типів підійдуть власний код або **Mapperly** з генерацією вихідного коду під MIT-ліцензією.

### Решта коротко

- **Command** інкапсулює запит в об’єкт і дає змогу параметризувати, ставити в queue (черга), журналювати й скасовувати операції. Запит MediatR є командою, як і будь-який `ICommand`, доданий до queue. Типовий приклад — стеки скасування й повторення дій.
- **Template Method** визначає в базовому класі каркас алгоритму й дає підкласам змогу перевизначати окремі кроки. Його використовують `ControllerBase` в ASP.NET і багато базових класів платформи. Це родич Strategy на основі успадкування (Strategy віддає перевагу композиції).
- **Chain of Responsibility** передає запит ланцюжком handlers, доки один із них не візьметься за нього. Так працює pipeline проміжного ПЗ ASP.NET Core: кожен компонент вирішує, обробити запит, перервати ланцюжок чи передати далі. Також на цьому побудовано поведінки pipeline MediatR і pipelines обробки повідомлень.
- **State** дає об’єкту змогу змінювати поведінку зі зміною внутрішнього стану, делегуючи роботу об’єкту стану. Це чистіше, ніж розкидати великий `switch (_state)` між методами. Типовий приклад — життєвий цикл замовлення (Pending → Paid → Shipped).
- **Visitor** відокремлює алгоритм від структури об’єктів, над якою він працює, і дає змогу додавати операції без зміни самих об’єктів. Він потужний, але відомий багатослівністю; його можна зустріти в компіляторах і обробці дерев виразів. Зіставлення зі зразком у C# (`switch` за типом) часто замінює його читабельніше.
- **Iterator** надає послідовний доступ до елементів, не відкриваючи внутрішню структуру. Це `IEnumerable<T>` / `IEnumerator<T>`, а `yield return` безкоштовно реалізує ітератори мовними засобами. Ви застосовуєте цей шаблон щоразу, коли пишете `foreach`.
- **Memento** зберігає внутрішній стан об’єкта для подальшого відновлення, не порушуючи інкапсуляцію. Його використовують системи скасування дій і знімки стану.

## Корпоративні та прикладні шаблони

Цих шаблонів немає в початковому каталозі GoF, але вони визначають повсякденну архітектуру .NET. Саме тут найбільше потрібне судження досвідченого розробника. Складніші з них — Specification і CQRS — розглянуто в [Розділі 21](#chapter-21-architecture).

### Repository та Unit of Work

Шаблон Repository абстрагує доступ до даних за інтерфейсом, схожим на колекцію (`GetById`, `Add`, `Remove`), тож бізнес-логіка не знає, чи зберігаються дані в SQL, документному сховищі або пам’яті. Unit of Work відстежує набір змін і фіксує їх однією атомарною транзакцією.

```csharp
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(int id);
    Task AddAsync(Order order);
    void Remove(Order order);
}

public interface IUnitOfWork
{
    IOrderRepository Orders { get; }
    Task<int> SaveChangesAsync();   // commit everything atomically
}
```

Ось нюанс, який має розуміти досвідчений розробник:

> **Велика суперечка про Repository поверх EF Core.** `DbContext` у EF Core *вже є* Unit of Work: він відстежує зміни й фіксує їх через `SaveChanges` (див. [DbContext і відстеження змін](#dbcontext-and-change-tracking) у Розділі 7), а `DbSet<T>` *вже є* репозиторієм — абстракцією колекції з можливістю запиту. Тож обгортання EF Core власними шарами Repository і Unit of Work часто означає абстрагувати абстракцію.

Аргументи **проти** власного репозиторію поверх EF Core:
- Абстракція часто протікає. Щоб зберегти ефективність, зрештою доводиться надавати `IQueryable`, який знову протягує семантику EF крізь вашу «абстракцію».
- Узагальнені реалізації `Repository<T>` зазвичай мають API за принципом найменшого спільного знаменника: або приховують потужні можливості EF (проєкції, `Include`, розділені запити), або незграбно відкривають їх знову.
- Заявлена перевага — «можна буде змінити базу даних» — майже ніколи не реалізується, а якби реалізувалася, відмінності запитів однаково зламали б вашу абстракцію.

Аргументи **на користь**:
- **Тестованість і чіткі межі** чистої/гексагональної архітектури: доменний шар залежить від `IOrderRepository`, а не від EF Core, тож інфраструктура не потрапляє до ядра.
- Тут можна розміщувати **іменовані запити, що виражають намір** (`GetOverdueOrdersAsync`), замість дублювання LINQ у застосунку.

> **Рекомендація:** не створюйте узагальнений репозиторій поверх EF Core за звичкою. Якщо потрібна межа, віддайте перевагу *конкретним* репозиторіям із змістовними методами для окремих сценаріїв, які повертають матеріалізовані результати (не `IQueryable`). Для багатьох застосунків практичний і чесний вибір — працювати безпосередньо з `DbContext` або виконувати запити через EF, а команди — через репозиторії. Вирішуйте, спираючись на потребу архітектури в доменній межі, а не на звичку.

### Options

Шаблон Options прив’язує розділ налаштувань до строго типізованого класу й впроваджує його там, де потрібно, замість повсюдного читання магічних рядків із `IConfiguration`. Механізм (прив’язування, відмінності між `IOptions<T>`, `IOptionsSnapshot<T>` та `IOptionsMonitor<T>`, перевірку) описано в [Шаблоні Options і прив’язуванні](#the-options-pattern-and-binding) Розділу 3. Саме проєктний аспект робить його шаблоном, а не просто зручністю. Кожен consumer (споживач) залежить від малого класу налаштувань для власної потреби (`SmtpOptions`, а не всього дерева налаштувань) — це застосування розділення інтерфейсів до параметрів. Клас є звичайним об’єктом, який тест можна створити без будь-яких налаштувань, а `.ValidateDataAnnotations().ValidateOnStart()` змушує некоректний параметр виявитися під час запуску, а не спричинити `NullReferenceException` о третій ночі. Використовуйте цей шаблон типово: так у сучасному .NET і налаштовують застосунки.

### Шаблон Result і програмування в стилі залізничних колій

Шаблон Result подає успіх і помилку як *явні значення, що повертаються*, а не як exceptions. Метод повертає `Result<T>` — або успішний результат зі значенням, або помилку. Це підхід для *очікуваних* збоїв, що є частиною звичайного потоку, а не exception: помилок перевірки, «не знайдено», порушень бізнес-правил.

```csharp
public readonly struct Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }

    private Result(bool ok, T? value, string? error)
    {
        IsSuccess = ok; Value = value; Error = error;
    }

    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(string error) => new(false, default, error);

    // The 'bind' that enables railway-oriented programming:
    // chain the next step only if we're still on the success track.
    public Result<TNext> Then<TNext>(Func<T, Result<TNext>> next)
        => IsSuccess ? next(Value!) : Result<TNext>.Failure(Error!);
}
```

**Програмування в стилі залізничних колій** — це метафора: уявіть дві паралельні колії — успіху й помилки. Кожна операція є стрілкою. Поки кроки успішні, ви рухаєтеся колією успіху; щойно один завершується помилкою, потяг переходить на колію помилки, усі наступні кроки пропускаються, а вихідна помилка передається до кінця. `Then` (також відомий як `Bind`) реалізує таке перемикання.

```csharp
Result<Order> result = ValidateOrder(request)
    .Then(ReserveInventory)
    .Then(ChargePayment)
    .Then(CreateShipment);

return result.IsSuccess
    ? Results.Ok(result.Value)
    : Results.BadRequest(result.Error);
```

> **Result чи exception?** `Result<T>` робить очікуваний збій частиною контракту: з `Order Process()` неможливо зрозуміти, що метод може завершитися помилкою, не прочитавши його тіло, тоді як `Result<Order> Process()` одразу це показує. Exceptions залиште для справді виняткових ситуацій, де продовження гірше за зупинку. У [«Exceptions чи Result: остаточно розберімося»](#exceptions-vs-result-settled-properly) Розділу 9 наведено компроміс і вимірювання вартості. Готові до продакшену реалізації надають бібліотеки **FluentResults** і **CSharpFunctionalExtensions**.

### Null Object і Guard Clauses (коротко)

- **Null Object:** замість повернення `null` і вимоги перевіряти його у викликачів поверніть нешкідливий об’єкт, який реалізує інтерфейс і нічого не робить. `NullLogger`, що мовчки відкидає повідомлення, дає змогу журналювати безумовно, не додаючи `if (logger is not null)`. Він замінює розкидані перевірки на поліморфізм, але застосовуйте його, лише коли «нічого не робити» справді є правильною поведінкою, а не для приховування відсутнього значення, з яким викликачі *мають* працювати.
- **Guard Clauses:** перевіряйте передумови на початку методу й одразу виходьте в разі помилки, не відступаючи від основного шляху виконання. `if (order is null) throw new ArgumentNullException(nameof(order));` на початку краще, ніж обгортати все тіло методу в `if`. Сучасний C# і бібліотеки на кшталт **Ardalis.GuardClauses** спрощують це: `Guard.Against.Null(order);` або `ArgumentNullException.ThrowIfNull(order);`. У .NET 8 додали групу вбудованих перевірок діапазону — `ArgumentOutOfRangeException.ThrowIfNegative(count)`, `ThrowIfZero(...)` і `ThrowIfGreaterThan(...)`, — тож для більшості передумов більше не потрібно власноруч писати `if`/`throw`. Guard clauses — невелика звичка з непропорційно великим впливом на читабельність.

## Чистий код і його запахи

Код читають набагато частіше, ніж пишуть, тож замовником є читач, а не компілятор. Наведені вище принципи описують *структурний* бік доброго коду; чистий код — це *локальний* бік, тобто те, як виглядають окремі назви, методи й файли зблизька. Неохайний код ускладнює кожну майбутню зміну й оцінювання.

> **Зрозумілість важливіша за хитромудрість.** Компілятор не винагороджує за щільний однорядковий вираз, а наступний розробник подумки прокляне його. Пишіть для людини, якій доведеться розібратися в цьому коді під тиском о другій ночі.

### Назви: найдешевша документація, яку ви напишете

Вдала назва безкоштовно виконує роботу коментаря й ніколи не застаріває; невдала — активно вводить в оману. Обирайте **назви, що виражають намір**: вони мають пояснювати, навіщо потрібна сутність і що вона робить, без пошуку відповіді в іншому місці.

```csharp
// BEFORE — every name forces a mental lookup
public List<int[]> GetThem()
{
    var list1 = new List<int[]>();
    foreach (var x in theList)
        if (x[0] == 4)
            list1.Add(x);
    return list1;
}
```

```csharp
// AFTER — the names carry the meaning
public List<Cell> GetFlaggedCells()
{
    var flaggedCells = new List<Cell>();
    foreach (var cell in gameBoard)
        if (cell.IsFlagged)
            flaggedCells.Add(cell);
    return flaggedCells;
}
```

Алгоритм ніяк не змінився. Змінилося те, що `cell.IsFlagged` повідомляє зміст, якого ніколи не передавало `x[0] == 4`.

A few конкретних правил охоплюють більшість випадків:

- **Без скорочень і кодів.** `custMgr`, `strName`, `bIsActive` заощаджують натискання клавіш, але змушують кожного читача додатково розшифровувати назву. У статично типізованій мові тип уже вказаний в оголошенні, а IDE доповнить назву.
- **Використовуйте назви, придатні для пошуку.** Окреме `7` не знайти через grep; `MaxRetryAttempts = 7` можна знайти, зрозуміти й змінити в одному місці.
- **Не вводьте в оману.** Не називайте щось `accountList`, якщо насправді це `Dictionary`.
- **Класи — іменники, методи — дієслова.** `InvoiceGenerator` і `CalculateTotal()`; клас із назвою `Process` порушує модель читача.
- **Одне слово для одного поняття.** Якщо в одному місці ви пишете `Fetch`, у другому `Retrieve`, а в третьому `Get`, читач гадатиме, чи є між ними змістовна різниця. Зазвичай її немає — оберіть одне слово.
- **Не змушуйте читача подумки зіставляти значення.** Називайте змінну циклу `customerIndex`, а не `i`. Однолітерні назви виправдані лише в крихітній загальноприйнятій області, наприклад у короткому лямбда-виразі LINQ.

### Функції: малі, цільові, чесні

Найнадійніше структурне правило читабельного коду: **функції мають бути малими й виконувати одну дію**. «Одну дію» легше відчути, ніж точно визначити, але корисним критерієм є **єдиний рівень абстракції**: не змішуйте політику високого рівня з низькорівневими механізмами. Якщо один рядок викликає `CalculatePricing(order)`, а наступний обробляє індекси рядка, це різні рівні, і їм, імовірно, місце в різних методах.

```csharp
// BEFORE — one function, three levels of abstraction, several responsibilities
public void ProcessOrder(Order order)
{
    if (order.Items.Count == 0) throw new InvalidOperationException("Empty order");

    decimal total = 0;
    foreach (var item in order.Items)
    {
        var line = item.UnitPrice * item.Quantity;
        if (item.Quantity >= 10) line *= 0.9m; // bulk discount
        total += line;
    }
    total += total * 0.2m; // VAT

    var conn = new SqlConnection(_connectionString);
    conn.Open();
    var cmd = new SqlCommand("INSERT INTO Orders ...", conn);
    cmd.ExecuteNonQuery();

    _smtp.Send(new MailMessage("shop@x.com", order.CustomerEmail, "Receipt", $"Total: {total}"));
}
```

```csharp
// AFTER — each function does one thing, all at one level of abstraction
public void ProcessOrder(Order order)
{
    ValidateOrder(order);
    var total = CalculateTotal(order);
    _orderRepository.Save(order, total);
    SendReceipt(order, total);
}

private static void ValidateOrder(Order order)
{
    if (order.Items.Count == 0)
        throw new InvalidOperationException("Cannot process an order with no items.");
}

private static decimal CalculateTotal(Order order)
{
    var subtotal = order.Items.Sum(LineTotal);
    return subtotal * (1 + VatRate);
}

private static decimal LineTotal(OrderItem item)
{
    var line = item.UnitPrice * item.Quantity;
    return item.Quantity >= BulkThreshold ? line * BulkDiscount : line;
}
```

Тепер `ProcessOrder` читається як зміст. Також зауважте, що магічні числа стали іменованими константами (`VatRate`, `BulkThreshold`, `BulkDiscount`) — до цього запаху ми ще повернемося.

Кілька правил для сигнатур функцій, здобутих нелегко:

- **Небагато параметрів.** Із нулем-двома працювати легко; понад три — це запах коду. Введіть *об’єкт параметрів*, який згрупує пов’язані аргументи в тип із назвою. Це читабельніше й захищає від типової помилки, коли аргументи передають не в тому порядку.
- **Без параметрів-прапорців.** Виклик `GenerateReport(true)` незрозумілий: true — *що саме*? Булевий параметр майже завжди означає, що функція виконує дві дії; розділіть її.
- **Уникайте вихідних параметрів.** Параметри `out` і `ref`, які змінюють змінні викликача, дивують читачів; натомість поверніть значення або невеликий запис. (Ідіоматичний шаблон `TryParse` — прийнятний exception.)
- **Розділення команд і запитів.** Метод має або *щось робити* (змінити стан і нічого не повертати), або *відповідати на запитання* (повернути значення й нічого не змінювати), але не обидва водночас. `if (SetAttribute("x"))` залишає читача в сумнівах: метод запитує чи виконує дію?
- **Без прихованих побічних ефектів.** `IsValid`, що нишком ініціалізує сеанс, або `GetUser`, що також оновлює час останньої активності, суперечать своїм назвам і стають плідним джерелом помилок.

### Коментарі: пояснюйте «чому», а не «що»

Коментар — це невелика невдача: визнання, що код самостійно не зміг виразити намір. Іноді цього не уникнути й коментар цілком доречний; часто ж це втрачена можливість перейменувати змінну або винести добре названий метод. Важлива відмінність — **чому, а не що**: код уже показує, *що* він робить, а повторення цього в коментарі створює шум, який застаріє щойно хтось змінить код, але не коментар.

```csharp
// BAD — restates the obvious, and will lie the day the code changes
// increment i by one
i++;

// GOOD — explains a non-obvious business reason the code cannot express
// The payment gateway rejects amounts over 10,000 in a single call,
// so we split large transfers into chunks. See INC-4821.
foreach (var chunk in transfers.Chunk(MaxTransferBatchSize))
    _gateway.Send(chunk);
```

Корисні коментарі пояснюють *намір* за неочевидним рішенням, попереджають про наслідки ("цей код не є потокобезпечним"), позначають чесно залишені борги `// TODO:` і `// HACK:`, пояснюють заплутаний регулярний вираз чи алгоритм або містять обов’язкові юридичні заголовки й ліцензійні повідомлення. Погані коментарі повторюють очевидне, вводять в оману чи застаріли, а найгірші — це **закоментований код**. Видаляйте мертвий код: система version control (контроль версій) його пам’ятає, а цвинтар закоментованих блоків змушує читачів боятися щось змінювати.

Для **публічних API** доречні XML-коментарі (`/// <summary>`): вони відображаються в IntelliSense, потрапляють до згенерованої документації й описують контракт, реалізації якого consumer не бачать. Документуйте публічну поверхню API, а приватні методи робіть зрозумілими без пояснень.

### Форматування, обробка помилок і щоденна дисципліна

Форматування потрібне не для краси, а щоб зменшити когнітивне навантаження на читача; його єдине правило — **послідовність**, і забезпечувати її вручну не слід. Зафіксуйте її в `.editorconfig` та аналізаторах Roslyn, щоб CI виявляв відхилення ([Форматування в CI](#formatting-in-ci), Розділ 13); суперечки про розташування фігурних дужок під час перегляду коду марнують цінну увагу людей, хоча інструмент безкоштовно владнає це питання. Також прагніть **локальності**: оголошуйте змінні поблизу першого використання, розміщуйте приватний допоміжний метод одразу під методом, який його викликає, а порожніми рядками відокремлюйте різні думки.

Обробка помилок визначає, наскільки читабельним буде *успішний* шлях. Віддавайте перевагу exceptions, а не кодам помилок: повернення `-1` чи `false` засмічує звичайний шлях і заохочує ігнорувати невдачу. Коли помилка *очікувана*, а не виняткова, використовуйте описаний вище шаблон Result. Ніколи не приховуйте exceptions: порожній `catch { }` ховає саме ту інформацію, яка знадобиться згодом (повне правило наведено в розділі 9, [Де перехоплювати](#where-to-catch)). Дійте швидко: перевіряйте дані на межі системи й одразу викидайте exception, замість того щоб дозволити некоректному значенню пройти вглиб системи; прибирайте вкладеність за допомогою вже розглянутих захисних перевірок. Нарешті, **не повертайте `null`** як звичайний результат: для "відсутності результатів" повертайте порожню колекцію (викликачі просто застосують `foreach`), для змістовної помилки — `Result<T>`, а типи nullable-посилань використовуйте, щоб компілятор принаймні виявляв решту ризиків.

Завершимо двома корисними звичками. **Правило бойскаута**: залишайте код трохи чистішим, ніж він був, — перейменуйте одну незрозумілу змінну, видаліть один закоментований блок під час кожного проходу; невеликі покращення накопичуються й долають ентропію. Також із підозрою ставтеся до **надмірно хитромудрого коду**: глибоко вкладений тернарний оператор чи LINQ-ланцюжок на п’ятнадцять рядків приємно писати, але нестерпно читати. Хитрість, яка заощаджує рядок, але забирає в читача хвилину, — невигідний обмін. Це принцип KISS у повсякденному коді, а досвідчений вибір — простий варіант, який команда зрозуміє одразу.

### Запахи коду: назви проблем

**Запах коду** — термін, популяризований Мартіном Фаулером і Кентом Беком, — це помітний на поверхні симптом глибшої проблеми проєктування. Запах — не помилка (код може працювати бездоганно) і не обов’язково щось неправильне; це *евристика*, привід придивитися уважніше. Найбільша користь терміна — у спільній мові: назвавши те, що турбує ("це Feature Envy"), ви водночас визначаєте стандартні рефакторинги, які допоможуть.

> **Запахи допомагають орієнтуватися, але не диктують рішення.** Запах — це привід *розглянути* рефакторинг, а не правило, яке змушує його виконувати. Іноді варіант із запахом справді найзрозуміліший, а примусове надання йому "чистої" структури лише погіршить ситуацію.

| Запах | Що заважає | Напрям рефакторингу |
|---|---|---|
| **Довгий метод** | Гортаєте код, втрачаєте думку й не можете перевірити середину | Виділити метод; розкласти умовний вираз |
| **Божественний клас / завеликий клас** | Усі зміни зосереджені тут; постійні конфлікти merge (злиття) | Виділити клас; перенести поведінку до об’єктів-співпрацівників |
| **Довгий список параметрів** | Виклики нечитабельні; аргументи легко переплутати | Об’єкт параметрів |
| **Дубльований код** | Виправляєте помилку двічі, але пропускаєте третю копію | Виділити метод/клас — одне місце для кожного фрагмента знань |
| **Feature Envy** | Метод постійно звертається до даних іншого класу | Перенести метод туди, де містяться ці дані |
| **Надмірна прив’язаність до примітивів** | Перевірки розкидані, некоректні значення вільно циркулюють | Об’єкт-значення |
| **Скупчення даних** | Та сама трійка полів усюди передається разом | Виділити клас (наприклад, `Address` чи `DateRange`) |
| **Хірургія дробовиком** | Невелика зміна потребує правок у багатьох файлах | Перенести метод/поле, щоб зосередити відповідальність |
| **Розбіжність змін** | Клас змінюється з не пов’язаних між собою причин (порушено SRP) | Виділити класи за напрямами змін |
| **Перемикання за типом** | Той самий `switch` дублюється; кожен новий випадок доводиться вишукувати | Замінити умовну конструкцію поліморфізмом; застосувати Strategy |
| **Ланцюжки викликів** | `a.B().C().D()` ламається, коли змінюється будь-яка ланка | Приховати делегат; звертатися до найближчого об’єкта (Деметра) |
| **Часова зв’язаність** | Методи працюють лише за прихованої послідовності викликів | Переробити API так, щоб неправильне використання не компілювалося |
| **Спекулятивна узагальненість** | Нікому не потрібні абстракції, за які платять усі | Згорнути ієрархію; видалити невикористані точки розширення (YAGNI) |
| **Коментарі як дезодорант** | Текст виправдовує код, який не може пояснити сам себе | Виділити метод із промовистою назвою; перейменувати |
| **Магічні числа / рядки** | Ніхто не наважується змінити незрозумілі літерали | Іменована константа; enum |

Чимало з цих запахів — локальні, видимі в коді прояви розглянутих принципів: розбіжність змін порушує SRP, дубльований код порушує DRY, ланцюжки викликів порушують закон Деметри, а спекулятивна узагальненість ігнорує YAGNI. Словник запахів і словник принципів описують ті самі сили з різного масштабу.

### Приклад рефакторингу: надмірна прив’язаність до примітивів → об’єкт-значення

Якщо представляти поняття предметної області простим примітивом, його правила розповзаються по всій кодовій базі, а некоректні значення можуть існувати.

```csharp
// BEFORE — an email is "just a string", so validation lives everywhere and nowhere
public class Customer
{
    public string Email { get; set; } // could be "", "not-an-email", null...
}

// callers must remember to validate, and they won't, consistently
if (!string.IsNullOrEmpty(input) && input.Contains("@"))
    customer.Email = input;
```

```csharp
// AFTER — a value object makes an invalid email unrepresentable
public sealed record EmailAddress
{
    public string Value { get; }

    public EmailAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Contains('@'))
            throw new ArgumentException($"'{value}' is not a valid email address.");
        Value = value.Trim().ToLowerInvariant();
    }

    public override string ToString() => Value;
}

public class Customer
{
    public EmailAddress Email { get; set; }
}
```

Тепер перевірка зосереджена в одному місці, система типів гарантує коректність кожного `EmailAddress`, а предметна область описана власними поняттями. Так само зникають скупчення даних: поля `street`/`city`/`postcode`/`country`, що постійно з’являються разом, варто об’єднати в об’єкт-значення `Address`.

Два інші ключові рефакторинги вже траплялися в цьому розділі. **Довгий метод → Виділити метод** — це розбір `ProcessOrder` у попередньому розділі про функції; прийом Фаулера *Replace Temp with Query* застосовує ту саму ідею до тимчасових змінних: перетворіть кожну повторно обчислювану змінну на іменований метод-запит, трохи збільшивши обчислення заради читабельності. А **Перемикання за типом → Поліморфізм** — той самий підхід, що й у прикладі Strategy/Open-Closed вище в розділі: кожен випадок стає класом, а новий випадок додає новий клас замість чергової правки `switch`, який непомітно дублюється по всій кодовій базі.

### Рефакторьте під контролем тестів, малими кроками

Зверніть увагу: правий стовпець таблиці запахів зводиться до невеликого набору названих прийомів — Extract Method/Class, Move Method, Parameter Object, Value Object, Replace Conditional with Polymorphism, Named Constant, — якими ви користуватиметеся постійно. Незмінне правило для кожного з них: **рефакторьте під контролем тестів, маленькими кроками.** За визначенням рефакторинг зберігає поведінку, а переконатися в цьому можна лише за успішного проходження тестів ([Розділ 8: Тестування](#chapter-8-testing)). Зробіть одну невелику зміну, запустіть тести, зафіксуйте її, а потім переходьте до наступної. Катастрофічний рефакторинг — це одна величезна зміна без тестів; це не рефакторинг, а переписування з надмірною самовпевненістю та без страховки.

Не потрібно й вручну вишукувати кожен запах: **аналізатори Roslyn** виявляють багато проблем під час збирання, **SonarQube**/SonarLint відстежують дублювання, складність і широкий набір запахів у кодовій базі, а метрики цикломатичної складності кількісно оцінюють, "наскільки заплутаний цей метод". Додайте ці засоби до pipeline ([Перевірки статичного аналізу в CI](#static-analysis-gates-in-ci), Розділ 13), щоб запахи з’являлися в pull requests (запити на злиття), а не під час інцидентів у продакшені.

Останнє практичне зауваження: із рефакторингом легко перегнути — розбити цілком читабельний метод на 30 рядків на вісім однорядкових, змушуючи читача стрибати файлом, щоб відновити одну думку, або так завзято виділяти абстракції, що заради усунення інших запахів ви створите спекулятивну узагальненість. Мета — не "нуль запахів". Мета — **читабельність і зручність змін**: код, який колега швидко зрозуміє та безпечно змінить. Виконуйте рефакторинг, якщо він допомагає досягти цього; якщо лише виконує пункт контрольного списку — залиште код як є.

> **Додаткова література:** *Clean Code* (Роберт К. Мартін), *Refactoring* (Мартін Фаулер), *The Pragmatic Programmer*.

## Багатошарова / N-рівнева архітектура

Якщо підняти розділення відповідальностей і інверсію залежностей із рівня класів до рівня проєктів, отримаємо дві структури, що трапляються майже в кожному рішенні .NET. Найдавніша й найінтуїтивніша з них — **багатошарова архітектура**. Відповідальності розподілено між шарами, і кожен шар спілкується лише з тим, що розташований безпосередньо під ним.

```
+---------------------------------------------+
|          Presentation (Controllers)         |  <- HTTP, UI, JSON
+---------------------------------------------+
|          Business Logic / Services          |  <- rules, workflows
+---------------------------------------------+
|          Data Access (Repositories)         |  <- EF Core, SQL
+---------------------------------------------+
|              Database / External            |
+---------------------------------------------+
```

Правило просте: залежності спрямовані *вниз*. Рівень представлення знає про бізнес-рівень, бізнес-рівень — про доступ до даних, і жодна залежність не спрямована вгору. Типове рішення .NET відображає це проєктами `MyApp.Web`, `MyApp.Services`, `MyApp.Data`.

Для багатьох застосунків це цілком розумний варіант за замовчуванням; вважати його застарілим — помилка новачка. Таку структуру легко зрозуміти, а нових учасників команди — швидко ввести в курс справ.

> **Пастка багатошарової архітектури:** бізнес-логіка залежить *вниз* від рівня даних. Отже, ключові правила предметної області пов’язані з Entity Framework, із `DbContext` і навіть зі структурою таблиць. Зміна способу зберігання даних спричиняє каскад змін у "чистому" бізнес-рівні. Такий переворот пріоритетів — найцінніший код (бізнес-правила) залежить від найменш цінного (інфраструктури) — і покликана виправити наступна група архітектур.

## Чиста, цибулева та гексагональна архітектури

Чиста архітектура, цибулева архітектура та гексагональна архітектура (порти й адаптери) — це назви від різних авторів, які описують по суті ту саму ідею. Сприймайте їх не як конкурентів, а зрозумійте спільний принцип і зверніть увагу на відмінності в термінології.

### Правило залежностей

Найважливіша ідея — **правило залежностей**: *залежності у вихідному коді спрямовані лише всередину, до предметної області.* Бізнес-правила в центрі нічого не знають про бази даних, веб-фреймворки чи queue повідомлень. Зовнішні кільця залежать від внутрішніх, але ніколи навпаки.

```
        +-------------------------------------------+
        |   Infrastructure / UI / DB / External     |   Frameworks & Drivers
        |   +-----------------------------------+   |
        |   |   Interface Adapters              |   |   Controllers, Presenters,
        |   |   (Controllers, Gateways)         |   |   Repository implementations
        |   |   +---------------------------+   |   |
        |   |   |   Application (Use Cases)  |   |   |   Orchestrates the domain
        |   |   |   +-------------------+   |   |   |
        |   |   |   |   Domain Entities  |   |   |   |   Enterprise rules
        |   |   |   |   (the core)       |   |   |   |
        |   |   |   +-------------------+   |   |   |
        |   |   +---------------------------+   |   |
        |   +-----------------------------------+   |
        +-------------------------------------------+
                  Dependencies point INWARD --->
```

Як спрямувати залежність *усередину*, якщо рівню застосунку потрібно зберігати дані в базі, розташованій у зовнішньому кільці? Застосувати описаний на початку розділу **принцип інверсії залежностей**. Рівень застосунку *визначає інтерфейс* `IOrderRepository`, який описує його потреби власними термінами. Інфраструктурний рівень *реалізує* цей інтерфейс. Тепер стрілка залежності спрямована від інфраструктури всередину, до інтерфейсу предметної області, хоча під час виконання виклик іде назовні. Це і є "порт" у Ports & Adapters: інтерфейс — порт, конкретний клас — адаптер.

- **Гексагональна архітектура (порти й адаптери)** підкреслює симетрію: застосунок — це шестикутник із портами з кожного боку. Керувальні адаптери (інтерфейс користувача, тести) надсилають запити через первинні порти; керовані адаптери (база даних, електронна пошта) викликаються через вторинні порти. Форма шестикутника означає лише "багато сторін, багато адаптерів".
- **Цибулева архітектура** наголошує на концентричних кільцях і напрямку залежностей усередину.
- **Чиста архітектура** (синтез Роберта Мартіна) додає назви кілець — сутності, сценарії використання, адаптери інтерфейсів і фреймворки — та чітко формулює правило залежностей.

Усі вони дають однаковий результат: **предметну область можна тестувати ізольовано, а компоненти на межах — замінювати.**

### Структура проєктів .NET

```
src/
  Domain/            <- Entities, Value Objects, domain events, interfaces
     (no dependencies on other projects)
  Application/       <- Use cases, DTOs, IOrderRepository, IEmailSender
     (references Domain only)
  Infrastructure/    <- EF Core, repositories, SMTP, Stripe client
     (references Application + Domain)
  Web/               <- ASP.NET controllers, DI wiring
     (references Application; wires up Infrastructure at startup)
```

Зверніть увагу: `Domain` не має залежностей від інших проєктів. А `Web` — точка входу — єдине місце, де відомі всі конкретні компоненти, оскільки воно відповідає за їх компонування ("корінь компонування", де налаштовується контейнер dependency injection).

```csharp
// Domain — pure, no framework types
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(OrderId id, CancellationToken ct);
    Task AddAsync(Order order, CancellationToken ct);
}

// Application — orchestrates, depends on the interface
public sealed class PlaceOrderHandler
{
    private readonly IOrderRepository _orders;
    public PlaceOrderHandler(IOrderRepository orders) => _orders = orders;

    public async Task<OrderId> Handle(PlaceOrderCommand cmd, CancellationToken ct)
    {
        var order = Order.Place(cmd.CustomerId, cmd.Lines);   // domain rules live in Order
        await _orders.AddAsync(order, ct);
        return order.Id;
    }
}

// Infrastructure — the adapter, references EF Core
public sealed class EfOrderRepository : IOrderRepository { /* ... uses DbContext ... */ }
```

> **Компроміс:** чиста архітектура забезпечує тестованість, гнучкість і модель предметної області, що відображає бізнес, а не схему бази даних. За це доводиться платити додатковими рівнями й формальностями: більшою кількістю проєктів та інтерфейсів, перетвореннями між DTO і сутностями. Для адміністративного інструмента CRUD це надмірне проєктування. Для системи зі складними й довговічними бізнес-правилами такі витрати багаторазово окупаються. **Співвідносіть формальності зі складністю предметної області, а не з модою.**

## Наостанок

Зверніть увагу, скільки цих шаблонів розчинилися у звичайному C#: Iterator став `yield`, Prototype — `with`, Observer — `event`, Strategy — `Func<>`, а Singleton — часом життя в DI. Це не збіг. У міру розвитку мови та її екосистеми вчорашні шаблони стають вбудованими можливостями сьогодення. Варто пам’ятати ті шаблони, яких мова *ще не* поглинула, і *принципи*, що лежать в основі всіх них.

Ставтеся до шаблонів невимушено, а принципів дотримуйтеся твердо. Коли виникає справжня проблема — розростається `switch`, клас виконує три завдання, тест неможливо написати через жорстко задану залежність, — застосовуйте шаблон лише для усунення цієї проблеми. Не зводьте собори з рівнів абстракції заради проблем, яких іще немає; так само співвідносіть архітектурні формальності зі складністю предметної області. Досвідченого розробника вирізняє не кількість застосованих шаблонів, а здатність не допускати зайвої складності в кодову базу.

## Перевірте на роботі

**Перевірте.** Відкрийте клас у вашому сервісі, який змінюється найчастіше (`git log --format= --name-only | sort | uniq -c | sort -rn | head`). Перелічіть причини його змін: якщо їх більше однієї, подумайте про Extract Class. Потім пошукайте `switch` за тим самим типом чи enum у кількох файлах, а також інтерфейси з єдиною реалізацією та без тестового двійника. Добре, коли кожен такий випадок можна обґрунтувати одним реченням. Погано — "може, колись знадобиться".

**Виміряйте.** У трьох наступних pull requests назвіть у коментарі перегляду кожен помічений запах із таблиці цього розділу й укажіть відповідний рефакторинг. Порахуйте, з якою кількістю погодився автор; ті, з якими він не погодився, підкажуть, де вашому обґрунтуванню бракує ще одного речення.
