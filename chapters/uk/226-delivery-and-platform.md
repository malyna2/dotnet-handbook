# Розділ 26: Доставка й платформа

У частині 1 ви навчилися збирати, тестувати й контейнеризувати одну службу ([розділ 13: Git і CI/CD](#chapter-13-git-and-cicd), [розділ 14: Контейнери й Linux](#chapter-14-containers-and-linux)). Цей розділ охоплює все між образом і користувачем, а також роботу з багатьма службами й командами. Ви навчитеся запускати службу .NET на віртуальній машині або в кластері Kubernetes і розуміти кожну перевірку, обмеження та маніфест; читати й виправляти складання Azure Pipelines та просувати один артефакт через середовища з контрольними воротами; безпечно випускати зміни за стратегіями поетапного, синьо-зеленого й канаркового deployment (розгортання) та feature flags (прапорці функцій); ставити перед серверами потрібні шлюзи й захищати їх від шкідливого трафіку; оцінювати команду платформи та її показники доставки.

Розділи відповідають шляху випуску. Спочатку — контракт, якого має дотримуватися служба, щоб працювати будь-де: дванадцять факторів. Далі — runtime (середовище виконання): віртуальна машина Linux під керуванням systemd, потім Kubernetes із маніфестами, Helm і Kustomize, service mesh (сервісна мережа) та .NET Aspire для внутрішнього циклу. Потім — спосіб доставки змін: Azure Pipelines, NuGet, стратегії deployment й feature flags. Далі — межа між користувачами та серверами: балансувальники навантаження, зворотні проксі, CDN, rate limiting (обмеження частоти) запитів і шкідливий трафік. Насамкінець — організаційний шар над усіма механізмами: інженерія платформ і вимірювання доставки без руйнування вимірювання.

## 12-факторний застосунок

Методологія Twelve-Factor App призначена для створення переносного, одноразового й зручного для хмари програмного забезпечення як послуги. Вона з'явилася до Kubernetes, але ідеально відповідає контейнеризованим службам .NET. Ось усі дванадцять факторів:

| Фактор | Короткий опис |
|---|---|
| 1. Кодова база | Один репозиторій, багато розгортань |
| 2. Залежності | Явно задекларовані через NuGet/`.csproj`; не припускайте, що щось уже встановлено |
| 3. Конфігурація | Надходить із середовища, а не з кодової бази |
| 4. Допоміжні служби | Бази даних, queue (черга), кеші — під'єднувані змінні ресурси |
| 5. Збирання, випуск, виконання | Артефакт → підключення конфігурації → виконання; не змінюйте запущений сервер |
| 6. Процеси | Без стану |
| 7. Прив'язка порту | Самодостатність: Kestrel обслуговує HTTP, зовнішній вебсервер не потрібен |
| 8. Паралельність | Масштабуйте кількість процесів, а не потужність машин |
| 9. Можливість швидкого завершення | Швидкий запуск, коректне завершення роботи |
| 10. Подібність розробки й продакшену | Підтримуйте однаковість середовищ (контейнери) |
| 11. Журнали | Потоки events (події) у stdout |
| 12. Адміністративні процеси | Разові завдання (міграції) використовують той самий код і конфігурацію |

Чотири фактори особливо важливі для .NET:

- **Конфігурація (3).** Рядки з'єднання й секрети надходять зі environment variables (змінні середовища) або сховища секретів, а не з `appsettings.json`, зафіксованого в репозиторії. [Шаруваті постачальники конфігурації .NET](#the-configuration-system) природно підтримують цей підхід, тож один артефакт без змін переходить між середовищами.
- **Відсутність стану й допоміжні служби (4, 6).** Між запитами нічого не зберігається в локальній пам'яті чи на диску; сеанси й кеші зберігаються в під'єднаних ресурсах. Це передумова horizontal scaling (горизонтальне масштабування) (8).
- **Можливість швидкого завершення (9).** Обробляйте `SIGTERM`, завершуйте поточні операції й звільняйте ресурси. Для цього призначений [pipeline (конвеєр) коректного завершення роботи](#processes-signals-graceful-shutdown) загального хоста; він дає змогу безпечно розгортати оновлення поетапно й еластично масштабуватися.
- **Журнали (11).** Структуровані журнали у stdout ([розділ 9](#chapter-9-exceptions-logging-and-first-diagnosis)); платформа збирає їх. Застосунок, який сам керує файлами журналів, конфліктує з кожним оркестратором.

> **Чому це важливо для досвідченого розробника .NET:** ці фактори — контракт для хмарного застосунку. Поруште його, і Kubernetes вас не врятує.

Найпростіше дотримуватися цих факторів на одному сервері Linux. Сьогодні зазвичай обирають контейнери, але чимало служб .NET досі працюють на звичайних віртуальних машинах, і корисно розуміти обидва способи.

## systemd: запуск застосунку .NET як служби

Якщо розгортати застосунок на звичайній віртуальній машині Linux, а не в контейнері, потрібно забезпечити його запуск під час завантаження системи, перезапуск після аварії та належне ведення журналів. Це робить **systemd** — система ініціалізації, яка керує службами. Опис служби зберігається у **файлі модуля** в `/etc/systemd/system/`.

```ini
# /etc/systemd/system/myapp.service
[Unit]
Description=My ASP.NET Core App
After=network.target

[Service]
WorkingDirectory=/var/www/myapp
ExecStart=/usr/bin/dotnet /var/www/myapp/MyApp.dll
Restart=always
RestartSec=10
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://localhost:5000

[Install]
WantedBy=multi-user.target
```

Потім керуйте службою:

```bash
sudo systemctl daemon-reload        # tell systemd to re-read unit files
sudo systemctl enable myapp         # start automatically on boot
sudo systemctl start myapp          # start it now
sudo systemctl status myapp         # is it running? recent log lines
sudo systemctl restart myapp        # restart after a deploy
```

`Restart=always` забезпечує відновлення після збою, а `User=www-data` запускає застосунок без привілеїв. systemd надсилає **SIGTERM** під час `stop`, тож тут застосовується те саме [коректне завершення роботи, що й у контейнері](#processes-signals-graceful-shutdown).

**Контейнер чи служба:** у контейнері systemd не використовують — роль диспетчера процесів виконує саме runtime контейнерів, а ваш застосунок стає PID 1. Для звичайного deployment на віртуальній машині використовуйте systemd; для контейнеризованих — політику перезапуску оркестратора. Не запускайте systemd усередині контейнера.

## Сценарії оболонки для автоматизації

Сценарій оболонки об'єднує команди у файл для повторного використання. Такі сценарії потрібні для розгортань, перевірки стану й зв'язування кроків CI. Ось коментований сценарій deployment й перевірки:

```bash
#!/usr/bin/env bash
# The line above (shebang) tells the OS to run this with bash.

set -euo pipefail
# -e  : exit immediately if any command fails
# -u  : error on use of an unset variable (catches typos)
# -o pipefail : a pipeline fails if ANY stage fails, not just the last

APP_DIR="/var/www/myapp"
HEALTH_URL="http://localhost:5000/health"

echo "Building..."
dotnet publish -c Release -o "$APP_DIR"

echo "Restarting service..."
sudo systemctl restart myapp

echo "Waiting for health check..."
for i in {1..10}; do
  if curl -sf "$HEALTH_URL" > /dev/null; then
    echo "App is healthy."
    exit 0
  fi
  echo "  attempt $i failed, retrying in 3s..."
  sleep 3
done

echo "App failed to become healthy." >&2
exit 1
```

Основні ідеї: **shebang** обирає інтерпретатор; **`set -euo pipefail`** — найважливіший рядок для надійного сценарію (швидке виявлення помилки й чітке повідомлення про неї); `$VAR` читає змінну; цикл `for` разом із `curl -sf` (без зайвого виводу, з помилкою за невдалого запиту) опитує endpoint (кінцева точка) стану; ненульовий код `exit` повідомляє CI про невдале deployment.

> **Рекомендація:** Починайте кожен нетривіальний сценарій із `set -euo pipefail`. Без нього помилка посередині буде проігнорована, а сценарій продовжить роботу й часто погіршить ситуацію. Цей один рядок робить ненадійні сценарії безпечними.

Одна віртуальна машина й політика перезапуску обслуговують один сервер. Для парку серверів потрібен інструмент, який вирішує, де запускати кожен процес, і підтримує його роботу.

## Основи Kubernetes

[Compose](#docker-compose-for-local-development) чудово працює на одному комп'ютері. Але в продакшені потрібні численні машини, автоматичний перезапуск аварійних застосунків, оновлення без простою, масштабування під навантаженням і самовідновлення після відмови сервера. Цим займається **оркестратор**, а **Kubernetes** (K8s) став фактичним стандартом.

Перехід від Compose до Kubernetes — це перехід від *імперативного* підходу до *декларативного*. Ви не кажете Kubernetes: «запусти цей контейнер». Ви оголошуєте: «хочу три репліки цього застосунку», а **цикли керування** Kubernetes постійно узгоджують реальний стан із бажаним: перезапускають несправні контейнери, переносять модулі з непрацюючих вузлів і роблять це без вашого втручання.

### Архітектура кластера

Кластер Kubernetes складається з **площини керування** (мозок) і **робочих вузлів** (м'язи).

Компоненти **площини керування**:

- **Сервер API** — вхідні двері. Усі команди й компоненти взаємодіють із кластером через цей REST API. `kubectl` — лише його клієнт.
- **etcd** — розподілене сховище «ключ-значення», у якому міститься весь стан кластера: єдине джерело істини.
- **Планувальник** — визначає, на якому вузлі запускати кожен новий модуль, з огляду на запити ресурсів, обмеження й правила спорідненості.
- **Менеджер контролерів** — запускає цикли керування (контролер Deployment, контролер ReplicaSet та інші), які наближають фактичний стан до бажаного.

На кожному **робочому вузлі** працюють:

- **kubelet** — агент вузла. Він спілкується із сервером API, запускає контейнери, призначені вузлу, і повідомляє про їхній стан.
- **Runtime контейнерів** — програмне забезпечення для запуску контейнерів (containerd, CRI-O).
- **kube-proxy** — налаштовує мережу вузла, щоб трафік Service надходив до правильних модулів.

> **Аналогія:** Площина керування — диспетчерська служби доставки, а вузли — вантажівки. Ви передаєте замовлення (маніфест) диспетчеру (серверу API). Диспетчер записує його (etcd), призначає вантажівку (планувальник), а водій (kubelet) виконує роботу. Якщо вантажівка зламається, диспетчер передасть її вантаж іншій; вам навіть не потрібно знати, яка саме це була вантажівка.

### Основні об'єкти від найменшого до найбільшого

**Pod** — найменша одиниця deployment. Модуль містить один або кілька контейнерів зі спільним мережевим простором імен (одна IP-адреса, той самий localhost) і сховищем. Зазвичай це один контейнер застосунку, іноді — допоміжні контейнери «sidecar». **Модулі короткоживучі й одноразові**: їх постійно створюють і знищують, щоразу з новою IP-адресою. Майже ніколи не створюйте Pod безпосередньо.

**ReplicaSet** — гарантує роботу визначеної кількості однакових реплік модулів. Якщо одна зупиняється, ReplicaSet створює їй заміну. За потреби цим об'єктом також рідко керують безпосередньо.

**Deployment** — об'єкт, з яким ви насправді працюєте. Він керує ReplicaSet і надає **декларативне оновлення й відкат**. Змініть образ у Deployment — і почнеться *поетапне оновлення*: нові модулі запускаються, система чекає, поки вони будуть готові, а потім зупиняє старі. Простоїв немає. Щось пішло не так? `kubectl rollout undo` поверне попередній ReplicaSet.

**Service** — модулі короткоживучі, і їхні IP-адреси змінюються, тому не можна спрямовувати клієнтів безпосередньо на модуль. Service — це **стабільна мережева endpoint**, фіксована віртуальна IP-адреса й DNS-ім'я, яка розподіляє трафік між динамічним набором модулів за мітками. Є три основні типи:

- **ClusterIP** (типовий варіант) — доступний лише *всередині* кластера. Так ваш API звертається до бази даних або один мікросервіс викликає інший.
- **NodePort** — відкриває статичний порт на IP-адресі кожного вузла й у простий спосіб надає зовнішній доступ до служби. Використовується переважно для розробки або як будівельний блок.
- **LoadBalancer** — створює справжній хмарний балансувальник навантаження (Azure/AWS LB) із зовнішньою IP-адресою. Стандартний спосіб відкрити службу в інтернеті в хмарі.

**Ingress** — окремий LoadBalancer для кожної служби коштує дорого й не дає гнучкої routing (маршрутизація). **Ingress** — це router (маршрутизатор) HTTP(S) рівня 7: одна вхідна точка, яка спрямовує запити за ім'ям хоста й шляхом (`api.example.com/orders` → служба замовлень, `/users` → служба користувачів), завершує TLS і працює за одним балансувальником. Для застосування правил у кластері потрібен **контролер ingress** (NGINX, Traefik).

**ConfigMap** — зберігає зовнішню неконфіденційну конфігурацію (feature flags, адреси вузлів з'єднання, рівні logging (журналювання)), щоб змінювати її без повторного збирання образу.

**Secret** — схожий на ConfigMap, але зберігає конфіденційні значення (паролі, ключі API, токени). Kubernetes зберігає їх у кодуванні base64.

> **Пастка:** Base64 — це *кодування, а не шифрування*. Кожен, хто має право читати Secrets, легко їх декодує. Увімкніть **шифрування даних у стані спокою** для etcd, обмежте доступ до Secret через RBAC, а для серйозних розгортань під'єднайте зовнішній менеджер секретів (Azure Key Vault, HashiCorp Vault), а не покладайтеся лише на звичайні Kubernetes Secrets.

**Namespace** — віртуальний кластер усередині кластера для ізоляції середовищ або команд (наприклад, `dev`, `staging`, `team-payments`). Назви мають бути унікальними в межах простору імен, а не всього кластера; для кожного простору імен можна встановити квоти ресурсів і правила доступу.

## YAML Kubernetes для deployment .NET

Розгорнімо API. Визначимо ConfigMap, Secret, Deployment і Service. Маніфести Kubernetes описуються декларативним YAML і застосовуються командою `kubectl apply -f`.

```yaml
# configmap.yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: myapi-config
  namespace: production
data:
  ASPNETCORE_ENVIRONMENT: "Production"
  Logging__LogLevel__Default: "Information"
  ConnectionStrings__Redis: "redis-service:6379"
---
# secret.yaml
apiVersion: v1
kind: Secret
metadata:
  name: myapi-secrets
  namespace: production
type: Opaque
stringData:
  # stringData lets you write plaintext; K8s base64-encodes it for you.
  ConnectionStrings__Postgres: "Host=postgres-service;Database=appdb;Username=app;Password=super-secret"
```

```yaml
# deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: myapi
  namespace: production
  labels:
    app: myapi
spec:
  replicas: 3
  selector:
    matchLabels:
      app: myapi
  template:                       # the pod template
    metadata:
      labels:
        app: myapi                # must match the selector above
    spec:
      containers:
        - name: myapi
          image: myregistry.azurecr.io/myapi:1.4.0
          ports:
            - containerPort: 8080
          envFrom:
            - configMapRef:
                name: myapi-config
            - secretRef:
                name: myapi-secrets
          resources:
            requests:             # guaranteed minimum, used for scheduling
              cpu: "100m"
              memory: "128Mi"
            limits:               # hard ceiling, enforced by cgroups
              cpu: "500m"
              memory: "256Mi"
          livenessProbe:
            httpGet:
              path: /healthz/live
              port: 8080
            initialDelaySeconds: 10
            periodSeconds: 10
          readinessProbe:
            httpGet:
              path: /healthz/ready
              port: 8080
            initialDelaySeconds: 5
            periodSeconds: 5
          startupProbe:
            httpGet:
              path: /healthz/live
              port: 8080
            failureThreshold: 30
            periodSeconds: 2
---
# service.yaml
apiVersion: v1
kind: Service
metadata:
  name: myapi-service
  namespace: production
spec:
  type: ClusterIP
  selector:
    app: myapi                    # routes to pods with this label
  ports:
    - port: 80                    # the service's port
      targetPort: 8080            # the container's port
```

Зверніть увагу на зв'язки: `selector.matchLabels` у Deployment і `labels` шаблону модулів мають збігатися, а `selector` Service використовує ту саму мітку, щоб знайти модулі, куди треба спрямувати трафік. **Мітки склеюють** ці слабко пов'язані об'єкти. Блок `envFrom` передає кожен ключ ConfigMap і Secret як environment variable. Завдяки угоді `__` ці значення одразу читаються конфігурацією .NET.

### Перевірки стану: liveness, readiness, startup

Kubernetes має знати дві різні речі про застосунок і використовує для цього три типи перевірок:

- **Перевірка liveness** — «контейнер *працює* чи завис?» Якщо вона не проходить, Kubernetes **завершує й перезапускає** контейнер. Використовуйте її для відновлення після deadlock (взаємне блокування) й зависання без відновлення. Вкажіть *просту* endpoint, яка перевіряє лише роботу самого процесу.
- **Перевірка readiness** — «чи готовий контейнер *прямо зараз обслуговувати трафік*?» Якщо перевірка не проходить, Kubernetes **вилучає модуль із балансувальника Service**, але *не* перезапускає його. Використовуйте цей стан, коли застосунок працює, але тимчасово не може обслуговувати запити — ще прогрівається або недоступна залежність. Після відновлення трафік повернеться.
- **Перевірка startup** — «чи *завершив застосунок запуск*?» Вона потрібна застосункам, які повільно стартують. Поки ця перевірка не пройде, liveness і readiness призупинені. Завдяки цьому повільний запуск не буде перерваний нетерплячою liveness-перевіркою. Налаштування `failureThreshold: 30 × periodSeconds: 2` дає до 60 секунд на запуск, перш ніж почне діяти liveness.

> **Пастка:** Не налаштовуйте liveness-перевірку на залежності нижчого рівня, як-от база даних. Якщо база короткочасно недоступна, liveness-перевірки всіх модулів одночасно завершаться невдало, Kubernetes перезапустить *їх усі* й перетворить короткий збій на каскадний. Стан залежностей перевіряйте через *readiness* (виводить модуль із трафіку), а не liveness (завершує його). Для такого поділу middleware (проміжне програмне забезпечення) перевірки стану ASP.NET Core підтримує окремі endpoints `/healthz/live` і `/healthz/ready` ([перевірки стану](#health-checks)).

### Запити й обмеження ресурсів

- **`requests`** визначає, які ресурси модуль *гарантовано отримає*. Планувальник використовує це значення, щоб знайти вузол із вільними ресурсами; вузол не прийме модуль, якщо запитаного ресурсу бракує.
- **`limits`** — це *жорстка верхня межа*. Якщо використання пам'яті перевищить ліміт, ядро виконає **OOM-kill** контейнера. За перевищення ліміту ЦП процес буде *обмежено* (сповільнено), а не завершено.

> **Рекомендація:** Завжди встановлюйте `requests` і `limits`. Без запитів планувальник може перевантажити вузол і позбавити ваш застосунок ресурсів. Без лімітів один процес, що вийшов з-під контролю, може зайняти цілий вузол і зупинити сусідні. Для передбачуваної гарантованої якості обслуговування встановіть однакові `requests` і `limits` пам'яті; для ЦП залиште запас між запитом і лімітом, бо ЦП допускає стискання.

### Горизонтальне автомасштабування модулів (HPA)

Фіксована кількість реплік марнує гроші вночі й не витримує пікового навантаження. **HPA** автоматично змінює кількість реплік на основі виміряних метрик, найчастіше — використання ЦП:

```yaml
# hpa.yaml
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: myapi-hpa
  namespace: production
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: myapi
  minReplicas: 3
  maxReplicas: 20
  metrics:
    - type: Resource
      resource:
        name: cpu
        target:
          type: Utilization
          averageUtilization: 70   # target 70% of the CPU *request*
```

HPA відстежує середнє завантаження ЦП модулів і, щойно воно перевищує 70% запиту ЦП кожного модуля, додає репліки (до 20). Коли навантаження знижується, кількість реплік зменшується (але не нижче 3). Пам'ятайте, що «70% використання» обчислюється від значення `requests` — ще одна причина правильно встановлювати запити. Для роботи HPA має бути встановлено додатковий компонент **metrics-server**, який надає ці числа.

## Helm і Kustomize: керування маніфестами в масштабі

Тепер у вас є набір YAML-файлів: deployment, service, configmap, secret, HPA, ingress. Помножте його на три середовища (dev, staging, prod), які відрізняються лише кількістю реплік, тегами образів та іменами хостів. Копіювання й редагування п'яти файлів для трьох середовищ — рецепт дрейфу конфігурації та помилок. Два інструменти розв'язують цю задачу по-різному.

**Helm** — це «менеджер пакетів для Kubernetes». **Chart** — набір маніфестів із *шаблонами* та файлом типових значень `values.yaml`. У шаблонах є заповнювачі, а значення можна перевизначити для кожного середовища:

```yaml
# templates/deployment.yaml (excerpt)
spec:
  replicas: {{ .Values.replicaCount }}
  template:
    spec:
      containers:
        - name: myapi
          image: "{{ .Values.image.repository }}:{{ .Values.image.tag }}"
```

```yaml
# values-prod.yaml
replicaCount: 5
image:
  repository: myregistry.azurecr.io/myapi
  tag: "1.4.0"
```

Розгортайте командою `helm install myapi ./mychart -f values-prod.yaml`. Helm відстежує кожне встановлення як версіонований **випуск**, тож команда `helm rollback myapi` одним кроком повертає весь застосунок до попереднього стану. Сильні сторони Helm — створення шаблонів і керування життєвим циклом; є тисячі готових chart (Postgres, Redis, контролери ingress), які можна встановити як залежності.

**Kustomize** дотримується протилежної філософії: жодних шаблонів і заповнювачів. Ви пишете звичайний правильний YAML як **основу**, а потім накладаєте **перевизначення**, які *змінюють* її для кожного середовища. Інструмент уже входить до `kubectl`:

```yaml
# overlays/prod/kustomization.yaml
resources:
  - ../../base
patches:
  - patch: |-
      - op: replace
        path: /spec/replicas
        value: 5
    target:
      kind: Deployment
      name: myapi
images:
  - name: myregistry.azurecr.io/myapi
    newTag: "1.4.0"
```

Застосуйте зміни командою `kubectl apply -k overlays/prod`. Основа залишається незмінною й правильною сама по собі, а накладання додає зміни поверх неї.

> **Рекомендація:** Обирайте **Kustomize**, коли середовища відрізняються простими структурними налаштуваннями (кількість реплік, теги, розмір ресурсів) і важлива читабельність звичайного YAML. Обирайте **Helm**, якщо потрібна справжня логіка шаблонів, треба поширювати пакований застосунок або відстежувати випуски й відкат. Багато команд поєднують обидва: Helm встановлює зовнішні залежності, а Kustomize — власні застосунки.

## Основні команди kubectl

`kubectl` — ваш головний інтерфейс до кластера. Ось команди, якими користуватиметеся щодня:

```bash
kubectl apply -f deployment.yaml        # create/update resources from a file
kubectl apply -k overlays/prod          # apply a kustomize overlay
kubectl get pods -n production          # list pods in a namespace
kubectl get pods -o wide                # ...with node and IP columns
kubectl describe pod myapi-abc123       # full detail + recent events (great for debugging)
kubectl logs myapi-abc123               # container logs
kubectl logs -f deploy/myapi            # follow logs across the deployment
kubectl exec -it myapi-abc123 -- sh     # shell into a container (if it has one)
kubectl rollout status deploy/myapi     # watch a rolling update progress
kubectl rollout undo deploy/myapi       # roll back to the previous revision
kubectl scale deploy/myapi --replicas=5 # imperative manual scale
kubectl port-forward svc/myapi-service 8080:80  # tunnel a service to localhost
kubectl get events --sort-by=.lastTimestamp     # recent cluster events
```

> **Рекомендація:** Коли модуль поводиться дивно, спершу виконайте `kubectl describe pod`. У розділі **Events** унизу зазвичай названо проблему (не вдалося отримати образ, не пройшла перевірка, недостатньо ресурсів, `CrashLoopBackOff`) ще до перегляду журналів.

## Service mesh: загальний огляд

Що більше мікросервісів, то більше спільних мережевих потреб: взаємний TLS між усіма службами, retries (повторні спроби) й тайм-аути, точний поділ трафіку для канаркових випусків і докладна телеметрія запитів. Реалізувати все це в кожному застосунку кількома мовами — повторювана й непослідовна робота.

**Service mesh** (Istio, Linkerd) переносить ці функції *із* застосунку в інфраструктуру. Вона додає поруч із кожним модулем **проксі sidecar** (зазвичай Envoy); увесь трафік проходить через ці проксі, які налаштовує центральна площина керування. Service mesh забезпечує взаємний TLS, автоматичні повтори й вимикачі, перемикання трафіку для канаркових і синьо-зелених випусків та єдину observability (спостережуваність) — **без жодної зміни коду .NET**.

Компроміс — справжня складність і додаткові ресурси на кожен модуль для проксі. **Linkerd** простіший і потребує менше ресурсів, а **Istio** потужніший і краще налаштовується, але має крутішу криву навчання. Для кількох служб service mesh не потрібна, але коли їх стає кілька десятків і з'являються суворі вимоги до безпеки й керування трафіком, вона стає привабливою. Поки що достатньо знати, що це таке й коли її використовувати.

## .NET Aspire

Для побудови distributed systems (розподілені системи) .NET треба керувати багатьма рухомими частинами: службами, базою даних, Redis, message broker (брокер повідомлень) і кодом, що з'єднує їх локально й у хмарі. Docker Compose ([розділ 14](#docker-compose-for-local-development)) не залежить від мови, а отже, нічого не знає про ваші проєкти .NET. **.NET Aspire** — це розроблена Microsoft цілісна платформа саме для такої роботи: готовий до хмари фреймворк для спостережуваних розподілених застосунків, що значно поліпшує *внутрішній цикл* (локальну розробку). Aspire має загальну доступність і власний графік версій, незалежний від щорічних випусків .NET, а не прив'язаний до однієї версії .NET.

Складові Aspire:

- **App Host** — проєкт C# (оркестратор), у якому топологію застосунку описують кодом, а не YAML: які проєкти, контейнери й хмарні ресурси існують та як вони з'єднані. Під час локальної розробки він запускає їх разом.

```csharp
// AppHost Program.cs
var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("cache");
var db    = builder.AddPostgres("pg").AddDatabase("orders");

var api = builder.AddProject<Projects.OrdersApi>("orders-api")
                 .WithReference(db)      // injects the connection string automatically
                 .WithReference(cache);

builder.AddProject<Projects.WebFrontend>("web")
       .WithReference(api);

builder.Build().Run();
```

Запустіть Aspire — він стартує ваші проєкти, запускає Postgres і Redis у контейнерах та з'єднує всі компоненти. Інші складові:

- **Пошук служб і конфігурація** — `WithReference` автоматично впроваджує рядки з'єднання й endpoints, тож служби знаходять одна одну без ручного керування конфігурацією. Це те саме, що ви вручну записували б у environment variables Compose, але з типобезпекою й автоматичним пошуком.
- **Компоненти й інтеграції** — відібрані пакети NuGet для типових допоміжних служб (Redis, PostgreSQL, RabbitMQ, ресурси Azure): стійкі бібліотеки клієнтів з інструментованою телеметрією, розумними типовими значеннями й вбудованими перевірками стану.
- **Панель моніторингу** — локальна панель для розробників із ресурсами, журналами, розподіленими трасами й метриками через OpenTelemetry одразу після запуску ([у розділі 25](#chapter-25-observability-and-testing-at-scale) пояснено, що саме ви бачите).
- **Deployment** — та сама модель App Host створює маніфести deployment й інтегрується з інструментами публікації в Kubernetes або Azure Container Apps (наприклад, Aspir8). Та сама модель застосунку C#, яка обслуговує внутрішній цикл, дає основу для deployment.

> **Як застосовувати Aspire:** це переважно шар **розробки й компонування**, який заохочує дотримуватися дванадцяти факторів. Це *не* runtime для продакшену й не service mesh. Для команди, яка створює модульний моноліт або кілька служб, він значно спрощує належну розподілену розробку .NET.

> **Рекомендація:** Використовуйте Aspire, щоб спростити локальну розробку й observability кількох служб та стандартизувати конфігурацію стійких інструментованих клієнтів між службами. Усе одно вивчайте Kubernetes і його маніфести: зрештою саме там працюватиме застосунок. Aspire доповнює ці знання, а не замінює їх.

> **Зв'язок із випускним проєктом:** Попередні розділи застосовуються в кроках 3 (Пакування в Docker) і [8 (Deployment за допомогою інфраструктури як коду)](#step-8-deploy-with-infrastructure-as-code) ShopCore з [розділу 44](#chapter-44-capstone-one-project-growing-up): ви пакуєте API багатостадійним Dockerfile і Compose, а потім запускаєте служби в керованому кластері Kubernetes або хмарному сервісі контейнерів.

Тепер відомо, де працює служба. Далі йтиметься про доставку змін: pipeline, який збирає й просуває їх, потрібні пакети та стратегії заміни робочої версії без відома користувачів.

## Azure Pipelines на практиці

Якщо ви професійно працюєте з .NET, імовірно, доведеться виправляти складання Azure Pipelines, а не GitHub Actions. Причини історичні й структурні: Azure DevOps з'явилася раніше за Actions, Microsoft першою додала до неї повноцінні завдання .NET, а згодом платформа отримала потрібні регульованим підприємствам засоби — погодження, перевірювані середовища, групи змінних із Key Vault і шаблони для всієї організації. Ідеї з [розділу 13: Git і CI/CD](#chapter-13-git-and-cicd), де показано GitHub Actions, теж працюють тут. Змінюється термінологія і, в одному важливому місці, форма файлу.

### Переклад із GitHub Actions: таблиця відповідників

| GitHub Actions | Azure Pipelines | Примітки |
|---|---|---|
| Workflow (`.github/workflows/*.yml`) | Pipeline (`azure-pipelines.yml`) | У репозиторії може бути кілька pipelines; кожен реєструють у UI й пов'язують із YAML-файлом. |
| — (відповідника немає) | **Stage** | Окремий рівень групування над завданнями зі своїми `dependsOn`, `condition` і змінними. |
| Job | Job | Та сама ідея: одиниця роботи, якій надається одна машина. |
| Step / action (`uses:`) | Step / task (`- task: X@1`) | Версію завдань задають основним номером (`@2`), а не тегом. `- script:` дає змогу виконати команду оболонки. |
| Runner (`runs-on:`) | Agent (`pool:`) | `pool: { vmImage: ubuntu-latest }` для вузла Microsoft або `pool: { name: my-pool }` для власного. |
| `secrets.FOO` | Група змінних, найкраще пов'язана з Key Vault | Групи визначають у Library й посилаються на них за назвою; секретні змінні приховані й *автоматично не передаються* як environment variables. |
| `environment:` із правилами захисту | `environment:` у завданні `deployment:` | Забезпечує погодження, обмеження робочого часу, перевірки Azure Function/REST і історію розгортань для кожного ресурсу. |
| Повторно використовуваний workflow / composite action | Шаблон (`extends:` / `template:`) | Шаблони отримують параметри *з типами* й розгортаються під час постановки в queue, а не під час виконання. |
| `actions/cache` | `Cache@2` | Така сама семантика restore-key, але інші назви параметрів. |
| `actions/upload-artifact` | `PublishPipelineArtifact@1` | Завантаження через `- download:` або `DownloadPipelineArtifact@2`. |

Важливо засвоїти одну структурну відмінність — **етапи (stages)**. У GitHub Actions є завдання й немає рівня над ними; випуск у кілька середовищ описують умовним ланцюжком `needs:` між завданнями. В Azure Pipelines цей рівень явний:

```
pipeline
 └── stage: Build            ── dependsOn: []
      └── job: build          ── runs on one agent
           └── step / task    ── runs in the job's working directory
 └── stage: DeployStaging    ── dependsOn: Build,  environment gate
 └── stage: DeployProd       ── dependsOn: DeployStaging,  approval required
```

Етап — це повноцінний об'єкт, тож його можна запустити повторно окремо; через середовища він має власні контрольні точки погодження, а UI показує процес випуску послідовністю блоків, а не графом завдань. Саме тому корпоративні процеси випуску — один раз зібрати, пройти чотири середовища, отримавши погодження на кожному, — зазвичай реалізують в Azure Pipelines, а не Actions.

### Повний `azure-pipelines.yml` для служби .NET

```yaml
trigger:
  branches:
    include: [ main, release/* ]
  paths:
    exclude: [ docs/*, README.md ]

pr:
  branches:
    include: [ main ]

variables:
  # A variable group defined in Library. Link it to Azure Key Vault and the
  # secret names in the vault become variables here, fetched at queue time.
  - group: order-api-secrets
  - name: buildConfiguration
    value: Release
  - name: NUGET_PACKAGES
    value: $(Pipeline.Workspace)/.nuget/packages
  - name: DOTNET_NOLOGO
    value: true

stages:
- stage: Build
  displayName: Build and test
  jobs:
  - job: build
    pool:
      vmImage: ubuntu-latest
    timeoutInMinutes: 30
    steps:
    - task: UseDotNet@2
      displayName: Install the SDK pinned in global.json
      inputs:
        packageType: sdk
        useGlobalJson: true

    - task: Cache@2
      displayName: Cache NuGet packages
      inputs:
        key: 'nuget | "$(Agent.OS)" | **/packages.lock.json'
        restoreKeys: |
          nuget | "$(Agent.OS)"
        path: $(NUGET_PACKAGES)

    - task: NuGetAuthenticate@1
      displayName: Authenticate to Azure Artifacts

    - script: dotnet restore --locked-mode
      displayName: Restore

    - script: dotnet build -c $(buildConfiguration) --no-restore
      displayName: Build

    - script: >
        dotnet test -c $(buildConfiguration) --no-build
        --logger trx --results-directory $(Agent.TempDirectory)/TestResults
        --collect:"XPlat Code Coverage"
      displayName: Test

    - task: PublishTestResults@2
      displayName: Publish test results
      condition: succeededOrFailed()      # publish even when tests failed
      inputs:
        testResultsFormat: VSTest
        testResultsFiles: '$(Agent.TempDirectory)/TestResults/**/*.trx'
        failTaskOnFailedTests: true

    - task: PublishCodeCoverageResults@2
      displayName: Publish code coverage
      condition: succeededOrFailed()
      inputs:
        summaryFileLocation: '$(Agent.TempDirectory)/TestResults/**/coverage.cobertura.xml'

    - script: >
        dotnet publish src/OrderApi/OrderApi.csproj
        -c $(buildConfiguration) --no-build
        -o $(Build.ArtifactStagingDirectory)/app
      displayName: Publish

    - task: PublishPipelineArtifact@1
      displayName: Publish pipeline artifact
      inputs:
        targetPath: $(Build.ArtifactStagingDirectory)/app
        artifactName: order-api

    # Named step + isOutput=true is what makes this readable from another stage.
    - script: echo "##vso[task.setvariable variable=version;isOutput=true]$(Build.BuildNumber)"
      name: meta
      displayName: Record the version being shipped

- stage: DeployStaging
  displayName: Deploy to staging
  dependsOn: Build
  condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))
  variables:
    # Runtime expression: only legal in a variables block or a condition.
    version: $[ stageDependencies.Build.build.outputs['meta.version'] ]
  jobs:
  - deployment: deployStaging
    environment: staging          # approvals and checks hang off this name
    pool:
      vmImage: ubuntu-latest
    strategy:
      runOnce:
        deploy:
          steps:
          - download: current
            artifact: order-api
          - task: AzureWebApp@1
            displayName: Deploy $(version) to App Service
            inputs:
              # Service connection using workload identity federation:
              # no client secret is stored anywhere.
              azureSubscription: sc-order-api-staging
              appName: order-api-staging
              package: $(Pipeline.Workspace)/order-api
```

У цьому прикладі є кілька неочевидних деталей.

**`UseDotNet@2` із `useGlobalJson: true`** встановлює саме SDK, указані у `global.json` репозиторію, а не випадкову версію з образу агента. Образи агентів оновлюються приблизно кожні три тижні, SDK додаються та вилучаються. Фіксація версії відрізняє відтворюване складання від такого, що без видимої причини зламається у вівторок.

**`deployment:` замість `job:`** відкриває доступ до середовищ. Завдання типу `deployment` записує, яка версія й куди потрапила, показує історію розгортань на сторінці середовища та дотримується налаштувань погоджень і перевірок — pipeline буквально призупиняється посеред запуску, доки відповідальна особа не натисне кнопку. `runOnce` — найпростіша стратегія; також є `rolling` і `canary`, які відповідають стратегіям deployment, описаним далі. Окремо взятий `job:` із ключем `environment:` не підтримується: контрольні точки доступні лише для завдань deployment.

**`condition: succeededOrFailed()`** для двох завдань публікації важлива, бо типовою умовою є `succeeded()`. Без неї після невдалих тестів результати не публікуються, а ви бачите червоне складання без звіту про тести — саме тоді, коли він потрібен найбільше.

### Відмінності Azure Pipelines

**`dependsOn` і `condition` взаємодіють так, що це часто створює проблеми.** Для кожного етапу й завдання неявно задано `condition: succeeded()`. Щойно ви задаєте власний `condition:`, то *замінюєте* умову за замовчуванням, а не додаєте до неї. Тож `condition: eq(variables['Build.SourceBranch'], 'refs/heads/main')` для етапу deployment без проблем запустить deployment після невдалого складання. Майже завжди потрібно використовувати `and(succeeded(), <your check>)`. `succeeded()` враховує залежності; `succeededOrFailed()` працює і після помилки, але не після скасування; `always()` виконується навіть після скасування — використовуйте його лише для справді необхідного очищення. За замовчуванням кожен етап залежить від етапу над ним у файлі; `dependsOn: []` прибирає цю залежність і запускає етап негайно, що дає змогу виконувати гілки паралельно.

**Вихідні змінні часто спричиняють запитання «чому моя змінна порожня?».** Мають виконуватися всі чотири умови. Для кроку-виробника потрібен `name:`. Команда logging має містити `isOutput=true`. Consumer (споживач) має використовувати вираз *під час виконання* `$[ ... ]`, який обчислюється лише в блоці `variables:` або `condition:`. Якщо вписати `$[ ... ]` у сценарій, нічого не станеться. І етап-споживач має залежати від етапу-виробника, бо об'єкт `stageDependencies` містить лише задекларовані залежності.

```yaml
# same job:        $(meta.version)
# different job:   $[ dependencies.build.outputs['meta.version'] ]
# different stage: $[ stageDependencies.Build.build.outputs['meta.version'] ]
```

> **Нюанс.** Якщо *виробником* є завдання `deployment:`, до ключа додається ще один сегмент для гачка життєвого циклу або ресурсу: `stageDependencies.Deploy.deployStaging.outputs['deployStaging.meta.version']`. Якщо змінна між етапами порожня, хоча все здається правильним, тимчасово додайте крок `- script: env` і перевірте фактичні назви змінних, які бачить агент, замість того щоб вгадувати вкладеність.

**Шаблони розгортаються, а не викликаються.** Шаблон — це фрагмент YAML, який підставляється під час постановки в queue. `- template: steps/build.yml` вставляє кроки на місце, а `extends:` створює весь pipeline на основі чужого каркаса. Параметри мають типи, і це справжня перевага перед рядковими входами Actions:

```yaml
# templates/dotnet-build.yml
parameters:
- name: projects
  type: string
  default: '**/*.csproj'
- name: configuration
  type: string
  default: Release
  values: [ Debug, Release ]      # rejected at queue time if violated
- name: runTests
  type: boolean
  default: true

steps:
- script: dotnet build ${{ parameters.projects }} -c ${{ parameters.configuration }}
- ${{ if eq(parameters.runTests, true) }}:
  - script: dotnet test -c ${{ parameters.configuration }} --no-build
```

```yaml
# azure-pipelines.yml
extends:
  template: templates/dotnet-build.yml@templates   # from a repository resource
  parameters:
    configuration: Release
```

Зверніть увагу на `${{ }}` (розгортання під час компіляції), `$[ ]` (під час виконання) та `$( )` (проста підстановка макросу). Три сигнатури — три етапи обчислення, і плутанина між ними породжує чимало незрозумілих помилок платформи. Значення `${{ }}` потрапляють до YAML до запуску агента, тож вони не можуть бачити нічого, створеного під час виконання.

> **Рекомендація.** Розміщуйте важливі для безпеки базові правила в шаблоні, на який pipelines посилаються через `extends`, і додайте перевірку *обов'язкового шаблону* до захищених середовищ і підключень служб. Шаблони `extends` можуть обмежувати кроки pipeline, тому запит на внесення змін не зможе додати крок, який викраде credentials (облікові дані) продакшену.

**Кешування допомагає лише за детермінованого відновлення пакетів.** `Cache@2` формує ключ за вмістом `packages.lock.json`. Якщо файлів блокування немає, ключ нестабільний або надто широкий і кешує не те; якщо вони є, але відновлення запускають без `--locked-mode`, NuGet однаково може вибрати інші версії, ніж записані в lock-файлі, й кеш перестане відповідати складанню. Lock-файли разом із `--locked-mode` також перетворюють загадковий збій через «хтось опублікував нову виправлену версію» на явну зміну, яку можна перевірити.

**Для приватних каналів потрібен `NuGetAuthenticate@1`.** Канали Azure Artifacts не є анонімними. Це завдання передає NuGet-постачальнику credentials ідентичності складання, тож команда `dotnet restore` працює. Без нього виникає `NU1101` (пакет не знайдено), бо неавтентифікований канал повертає порожню відповідь, а не 401. Якщо канал розміщено в іншій організації, також потрібне підключення служби, назву якого слід указати в параметрі `nuGetServiceConnections` завдання.

**Підключення служб — межа доступу до credentials.** Це збережена ідентичність із дозволами, яку завдання використовують для підключення до Azure, AWS, реєстрів Docker чи Kubernetes. Раніше зберігався секрет клієнта суб'єкта-служби, який потрібно було регулярно обертати. Сучасний варіант — **федерація ідентичності workload (робоче навантаження)**: підключення довіряє токенам, які організація Azure DevOps видає для певного підключення служби. Під час запуску агент обмінює короткоживучий токен OIDC на маркер доступу Azure, і *секрету, який можна викрасти чи обертати, не існує*. Переведіть підключення Azure на федерацію ідентичності workload: так зникає ціла категорія інцидентів. Це той самий принцип, що й у рекомендації щодо managed identity (керована ідентичність) з [розділу 13: Секрети в pipelines](#secrets-in-pipelines). Ланцюг довіри й умову політики довіри, яка є всією межею безпеки, докладно розглянуто в [розділі 27: Zero trust (нульова довіра) та ідентичність workload](#zero-trust-and-workload-identity).

### Читання й виправлення складання

Зазвичай складання — не проблема проєктування, а проблема читання. Досвідчений інженер діагностує червоний pipeline за дві хвилини, а початківець прокручує журнал двадцять хвилин.

**Знайдіть першу помилку, а не останню.** Це найефективніша звичка. Якщо `dotnet restore` завершився помилкою, каталог пакетів неповний, тож під час компіляції з'являться десятки `CS0246: The type or namespace name 'X' could not be found`. Це все шум. Вебінтерфейс відкриває кінець журналу — саме з неправильного боку. Згорніть завдання, знайдіть перше з червоною піктограмою та прочитайте перший рядок `##[error]`.

**Знайте маркери журналу.** Агент структурує журнали командами: `##[error]` і `##[warning]` перетворюються в UI на червоне й жовте, `##[section]` починає завдання, а `##[group]`/`##[endgroup]` згортають його частину. Список завдань ліворуч у перегляді запуску — це покажчик; для кожного є тривалість і код завершення. Сіре завдання, що тривало 0 секунд, було *пропущене* через умову, а не виконане успішно. Ця відмінність пояснює чимало випадків «але ж я опублікував артефакт».

**Увімкніть налагоджувальне logging.** Поставте pipeline у queue зі змінною `system.debug`, що має значення `true` (поле «Variables» у вікні запуску). З'являться рядки `##[debug]` із визначеними значеннями змінних, точними командами кожного завдання, результатами умов і рішеннями щодо відповідності шаблонам файлів. Якщо шаблон `testResultsFiles` нічого не знайшов, так ви побачите фактичний каталог, у якому шукало завдання.

**Завантажте необроблені журнали.** Вебперегляд скорочує довгий вивід і має проблеми з журналами в кілька мегабайт. «Завантажити журнали» для запуску дає архів ZIP з окремим текстовим файлом для кожного завдання. У них можна виконувати пошук, вони повні й це єдиний надійний спосіб прочитати журнал на 200 МБ від говіркого MSBuild із рівнем `/v:diag`.

**Повторно запускайте лише те, що не пройшло.** Обирайте «Rerun failed jobs», а не ставте в queue весь pipeline. Успішні етапи буде повторно використано, а ви заощадите час і збережете докази, які аналізували. За нестабільної інфраструктури це правильний перший крок; для *нестабільного тесту* це спосіб приховати справжню помилку, тож залиште відповідну примітку.

**Відтворіть проблему локально на тому самому SDK.** Прочитайте `global.json`, установіть точно такий SDK і виконайте ті самі команди, що й pipeline, скопіювавши їх із журналу, а не вгадуючи. Залишаються дві відмінності: агент отримує чистий клон (перш ніж стверджувати, що помилку відтворено, локально виконайте `git clean -xdf`), а агент працює на Linux, тоді як ваша машина може бути на Windows чи macOS. Це змінює регістр шляхів, обмеження довжини імен файлів і кінці рядків.

### Типові збої pipelines .NET

| Симптом | Причина | Виправлення |
|---|---|---|
| `NU1101: Unable to find package X` | Каналу з пакетом немає в `nuget.config` або агент не автентифікований, тож канал повертає порожню відповідь | Додайте канал; перед відновленням запустіть `NuGetAuthenticate@1`; надайте ідентичності складання роль Reader для каналу |
| `NU1605: Detected package downgrade` | Транзитивна залежність вимагає вищу версію, ніж фіксує пряме посилання `PackageReference` | Підвищте пряму залежність щонайменше до потрібної транзитивної версії або централізуйте версії через `Directory.Packages.props` |
| `MSB3277: conflicts between different versions of the same assembly` | Два пакети пов'язують різні основні версії однієї збірки | Знайдіть версію-переможця у виводі `/v:detailed`, уніфікуйте версії через CPM і використовуйте `binding redirects`/`AutoGenerateBindingRedirects` лише для цільових платформ .NET Framework |
| `A compatible .NET SDK was not found` / невідповідність `global.json` | Закріпленої версії SDK немає в образі агента | Налаштуйте `UseDotNet@2` із `useGlobalJson: true` або додайте до `global.json` `rollForward: latestFeature` |
| `The active test run was aborted` | Процес хоста тестів аварійно завершився: переповнення стека через рекурсію, `AccessViolation` у нативній залежності або `Environment.Exit` у тесті | Повторіть із `--blame-crash --blame-hang-timeout 5m`; у створеному файлі послідовності буде названо тест, який зупинив хост |
| Тести Testcontainers не можуть «під'єднатися до демона Docker» | Завдання працює на агенті `windows-latest`, у якому немає демона Linux Docker для контейнерів Linux | Перенесіть завдання інтеграційного тестування на `ubuntu-latest` або використовуйте власний агент із Docker — див. [розділ 8: Тестування](#chapter-8-testing) |
| `No space left on device` під час складання | Загалом агенти Microsoft дають близько 10 ГБ; багатошарові складання Docker, кеш NuGet і результати покриття швидко займають місце | Очищайте систему між кроками (`docker system prune -af`), не використовуйте непотрібну публікацію `--self-contained` або перейдіть на власний агент |
| `The job running on agent ... exceeded the maximum time of 60 minutes` | Безкоштовний рівень обмежує завдання приватного проєкту 60 хвилинами незалежно від `timeoutInMinutes` | Розбийте роботу на паралельні завдання, активніше використовуйте кеш або придбайте паралельне завдання (ліміт зросте до 360 хвилин) |

> **Пастка.** `timeoutInMinutes: 120` нічого не змінює для завдання на безкоштовному рівні агента Microsoft. Перемагає обмеження платформи, і через 60 хвилин завдання зупиняється із повідомленням, схожим на помилку конфігурації, хоча насправді йдеться про оплату. Розділити довгий набір тестів на два завдання зазвичай дешевше, ніж купувати ліцензію.

### Azure Pipelines чи GitHub Actions?

Обидві зрілі платформи добре збирають .NET. Чесна відповідь залежить від того, де розміщено код і керування.

| Обирайте Azure Pipelines, коли | Обирайте GitHub Actions, коли |
|---|---|
| Джерельний код у Azure Repos або робочі завдання й випуски відстежуються в Azure Boards | Код розміщено в GitHub і ви хочете мати в одному місці перевірки PR, випуски й перегляд коду |
| Потрібне поетапне deployment з погодженням для кожного середовища, audit log (журнал аудиту) й обов'язковими перевірками шаблонів | Процес deployment досить простий, щоб описати його ланцюжком завдань |
| Потрібен загальноорганізаційний шаблон `extends`, обов'язковий для pipelines | Потрібно швидко зібрати pipeline із дій Marketplace |
| Потрібні власні агенти у корпоративній мережі або Windows-агенти зі спеціальними інструментами | Достатньо хостованих виконавців або вже використовуються виконавці Actions |
| Відповідність вимогам вимагає іменного запису погодження для кожного deployment в продакшен | Достатньо правил захисту середовищ |

Практичний компроміс поширений і працює добре: код і перевірки pull request залишаються в GitHub Actions, де вже працюють розробники, а етапи deployment з важливими погодженнями й аудитом передаються Azure Pipelines. Обидві платформи можуть отримати той самий незмінний артефакт з одного реєстру. Саме для цього збирають один раз і послідовно просувають артефакт, а тому вибір менш важливий, ніж здається.

## NuGet докладно

NuGet — менеджер пакетів .NET. Досвідчений інженер має впевнено почуватися і під час використання пакетів, і під час їх створення.

**Використання пакетів.** Елементи `PackageReference` у `.csproj` оголошують залежності. `dotnet restore` читає їх, визначає граф залежностей і завантажує пакети до глобального кешу. Увімкнення файла блокування (`RestorePackagesWithLockFile` зі значенням true) створює `packages.lock.json` і фіксує точні визначені версії. Завдяки цьому CI виконує детерміноване й відтворюване відновлення, а ключі кешу залишаються стабільними.

**Створення й публікація.** Для бібліотеки `dotnet pack` створює файл `.nupkg`. Метадані пакета зберігаються в `.csproj`:

```xml
<PropertyGroup>
  <PackageId>Contoso.Ordering.Client</PackageId>
  <Version>2.3.1</Version>
  <Authors>Contoso Platform Team</Authors>
  <Description>Typed client for the Ordering API.</Description>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
</PropertyGroup>
```

Потім опублікуйте його в каналі:

```bash
dotnet nuget push ./nupkgs/Contoso.Ordering.Client.2.3.1.nupkg \
  --api-key $NUGET_API_KEY \
  --source https://api.nuget.org/v3/index.json
```

**Приватні канали.** Внутрішні бібліотеки зазвичай не слід публікувати в загальнодоступному nuget.org. Для організації їх зберігають у приватних каналах: Azure Artifacts, GitHub Packages, MyGet або власному каналі. Файл `nuget.config` у корені репозиторію спрямовує відновлення до потрібних каналів:

```xml
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="contoso" value="https://pkgs.dev.azure.com/contoso/_packaging/internal/nuget/v3/index.json" />
  </packageSources>
</configuration>
```

> **Пастка — плутанина залежностей:** Якщо назва внутрішнього пакета є й у загальнодоступному каналі, неправильно налаштоване відновлення може завантажити *публічний* пакет, який міг розмістити зловмисник. Захистіться від цього належним налаштуванням джерел і резервуванням префіксів ідентифікаторів пакетів у публічних каналах. У [розділі 27](#dependency-confusion-and-why-pinning-does-not-fix-it) докладно розглянуто атаку й повний набір засобів захисту.

## Стратегії deployment

Перевірене складання артефакту — лише половина роботи; від того, *як* ви заміните запущену версію, залежить, чи помітять це користувачі.

**Поетапне deployment** поступово замінює екземпляри. У парку з десяти серверів ви оновлюєте по два, даючи новій версії обслуговувати трафік, поки решта працює на старій, доки не оновляться всі. Додаткові потужності не потрібні, але деякий час трафік обслуговують обидві версії, тож застосунок і база даних мають це витримувати.

**Синьо-зелене deployment** використовує два повні середовища: *синє* (поточне, робоче) й *зелене* (нове). Розгорніть версію в зеленому середовищі, перевірте окремо, а потім одним атомарним перемиканням router спрямуйте весь трафік до зеленого. Відкат миттєвий: перемкніться назад на синє. Ціна — подвійна інфраструктура на час переходу.

**Канаркове deployment** спершу спрямовує до нової версії невелику частину трафіку, наприклад 5%, і відстежує частку помилок, затримку й бізнес-показники. Якщо канарка працює нормально, поступово збільшуйте частку до 25%, 50%, 100%. Якщо виникають проблеми, поверніть увесь трафік до стабільної версії, обмеживши кількість користувачів, які їх побачили. Канаркові випуски добре поєднуються з feature flags і якісною observability.

> **Рекомендація:** Яку б стратегію ви не обрали, зробіть **відкат дешевшим і швидшим за виправлення в майбутньому**. Зрілість процесу deployment визначається не відсутністю збоїв, а можливістю за секунди повернути невдалий випуск без надзвичайних зусиль.

**Керування артефактами** лежить в основі всіх цих стратегій. Створіть артефакт *один раз* — образ контейнера, пакет NuGet або опублікований ZIP-архів, — збережіть у реєстрі чи каналі артефактів і просувайте *той самий* незмінний артефакт середовищами (dev → staging → production). Позначайте його SHA коміту або SemVer, щоб точно знати, що працює. Повторне складання для кожного середовища знову створює ризик відмінностей між staging і production.

## Feature flags

Розробка на основі магістральної гілки й безперервне deployment спираються на розділення *deployment* й *випуску*. Ви зливаєте й розгортаєте незавершений або ризикований код, але приховуєте його за **feature flag**, доки навмисно не ввімкнете — для всіх або вибраної групи користувачів.

У .NET для цього є повноцінна підтримка через **`Microsoft.FeatureManagement`**:

```csharp
// Registration
builder.Services.AddFeatureManagement();

// Usage
public class CheckoutController(IFeatureManager features) : ControllerBase
{
    public async Task<IActionResult> Checkout()
    {
        if (await features.IsEnabledAsync("NewPricingEngine"))
            return Ok(await _newPricing.QuoteAsync());

        return Ok(await _legacyPricing.QuoteAsync());
    }
}
```

Прапорці налаштовуються зовні — через `appsettings.json`, Azure App Configuration або спеціалізовану платформу на кшталт **LaunchDarkly**, яка додає правила вибору (увімкнути для внутрішніх користувачів або 10% трафіку), audit log й миттєві вимикачі без повторного deployment. Саме так канарковий випуск можна ввімкнути для невеликої частки користувачів і поступово її збільшувати.

> **Пастка:** Якщо ніколи не видаляти feature flags, вони стають боргом. Кодова база з застарілими прапорцями перетворюється на нечитабельний лабіринт неактивних гілок. Відстежуйте прапорці та видаляйте і сам прапорець, і програшну гілку після повного й стабільного випуску функції.

Випущена версія все ще відділена від користувачів шарами інфраструктури, і перед кожним користувачем також має бути шар захисту.

## Балансувальники навантаження, зворотні проксі, шлюзи API й CDN

Між користувачами й серверами є шар інфраструктури для розподілу, захисту й прискорення трафіку. Досвідчені інженери мають знати призначення кожного компонента.

### Балансувальники навантаження: L4 і L7

**Балансувальник навантаження** розподіляє вхідні запити між набором серверів, забезпечуючи масштабування й відмовостійкість. Ключова відмінність — рівень моделі OSI, на якому він працює:

- **Рівень 4 (транспортний)** розподіляє трафік за IP-адресою та портом TCP/UDP. Він надзвичайно швидкий, бо лише пересилає пакети й не аналізує вміст; HTTP-шлях, заголовки й cookie для нього невидимі. Це як адміністратор, який спрямовує виклики лише за лінією, на яку вони надійшли.
- **Рівень 7 (прикладний)** розуміє HTTP. Він маршрутизує запити за шляхом URL (`/api` → служба A, `/images` → служба B), ім'ям хоста, заголовками чи cookie (для закріплення сеансів), завершує TLS і переписує запити. Потребує більше ЦП, але дає набагато більше гнучкості.

### Зворотні проксі й YARP

**Зворотний проксі** стоїть перед серверами й пересилає до них клієнтські запити, часто додаючи завершення TLS, стискання, кешування та зміну заголовків. (*Прямий* проксі стоїть перед *клієнтами*, а *зворотний* — перед *серверами*.) Класичний вибір — **nginx**.

У світі .NET **YARP (Yet Another Reverse Proxy)** — це набір інструментів зворотного проксі від Microsoft, з якого створюють власні проксі на основі застосунку ASP.NET Core. Він особливо корисний, коли логіку проксі потрібно виразити на C# і вбудувати в наявне middleware. Мінімальна конфігурація:

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();
app.MapReverseProxy();
app.Run();
```

```json
// appsettings.json
{
  "ReverseProxy": {
    "Routes": {
      "api-route": {
        "ClusterId": "api-cluster",
        "Match": { "Path": "/api/{**catch-all}" }
      }
    },
    "Clusters": {
      "api-cluster": {
        "LoadBalancingPolicy": "RoundRobin",
        "Destinations": {
          "d1": { "Address": "https://backend1.internal:5001/" },
          "d2": { "Address": "https://backend2.internal:5002/" }
        }
      }
    }
  }
}
```

Ця конфігурація спрямовує всі запити `/api/` до двох серверів за алгоритмом round-robin і з вбудованими перевірками стану — це балансувальник L7 та зворотний проксі лише в кількох рядках.

### Шлюзи API

**Шлюз API** — спеціалізований зворотний проксі, що централізує спільні функції API: автентифікацію, rate limiting запитів, об'єднання запитів, керування ключами API, версіями й перетворенням протоколів. У мікросервісній архітектурі він надає клієнтам єдину вхідну точку замість десятків окремих служб. YARP часто слугує основою для його створення.

### CDN і заголовки кешування

**CDN (Content Delivery Network — мережа доставки вмісту)** — глобально розподілена мережа серверів кешування (граничних вузлів), які зберігають копії вмісту ближче до користувачів. Користувач у Токіо звертається до вузла в Токіо, а не до джерела у Вірджинії; затримка різко зменшується, а джерело отримує менше запитів. CDN агресивно кешують статичні ресурси й дедалі частіше — динамічні відповіді та відповіді API.

CDN і браузери виконують **заголовки кешування** HTTP:

- **`Cache-Control`** — головний перемикач: `max-age=3600` (кешувати годину), `no-cache` (перевіряти перед використанням), `no-store` (ніколи не кешувати конфіденційні дані), `public`/`private` (спільний CDN може кешувати чи лише браузер користувача?), `immutable` (не перевіряти повторно для ресурсів із відбитком).
- **`ETag`** — відбиток вмісту (хеш або версія). Браузер зберігає його, а в наступному запиті надсилає `If-None-Match: "<etag>"`. Якщо вміст не змінився, сервер відповідає `304 Not Modified` без тіла — клієнт використовує свою кешовану копію, а сервер не передає дані повторно. `Last-Modified`/`If-Modified-Since` працює так само, але на основі часової мітки.

```csharp
app.MapGet("/report/{id}", (int id, HttpContext ctx) =>
{
    var report = GetReport(id);
    var etag = $"\"{report.Version}\"";

    if (ctx.Request.Headers.IfNoneMatch == etag)
        return Results.StatusCode(StatusCodes.Status304NotModified);

    ctx.Response.Headers.ETag = etag;
    ctx.Response.Headers.CacheControl = "public, max-age=60";
    return Results.Ok(report);
});
```

> **Рекомендація:** Додавайте відбиток до назви статичних ресурсів (`app.a1b2c3.js`) і віддавайте їх із `Cache-Control: immutable, max-age=31536000`. Оскільки з новим вмістом змінюється назва файла, його можна кешувати назавжди без ризику застарілості. Для HTML і відповідей API, що змінюються, залиште короткі або `no-cache` строки.

## Rate limiting запитів і тайм-аути на периферії

На мережевій периферії потрібні два засоби захисту: від зловживань і від самої системи.

**Rate limiting запитів** обмежує кількість запитів клієнта за певне вікно й у разі перевищення повертає `429 Too Many Requests` (бажано із заголовком `Retry-After`). Це захищає від зловживань, клієнтів із нескінченними повторами й каскадного перевантаження. Поширені алгоритми: **фіксоване вікно**, **ковзне вікно**, **відерце токенів** (дозволяє короткі сплески до розміру відерця та постійно його поповнює) й обмеження **паралельності**. В ASP.NET Core є вбудоване middleware:

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddTokenBucketLimiter("api", o =>
    {
        o.TokenLimit = 100;
        o.TokensPerPeriod = 20;
        o.ReplenishmentPeriod = TimeSpan.FromSeconds(1);
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});
app.UseRateLimiter();
```

**Тайм-аути** не дають повільній або недоступній залежності назавжди займати ваші ресурси. Обмежуйте тривалість кожного мережевого виклику. Без тайм-аутів одне зависле зовнішнє джерело може вичерпати thread pool (пул потоків) чи з'єднань і зупинити всю службу — класичний каскадний збій. Встановлюйте їх явно:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var response = await client.GetAsync(url, cts.Token);
```

> **Рекомендація:** Поєднуйте тайм-аути, retries (**експоненційне збільшення інтервалу й випадковий зсув**, щоб повтори не спричиняли сплеск одночасно) та **circuit breakers (автоматичні вимикачі)** (перестають перевантажувати несправну залежність) — три основи відмовостійкості. У .NET `Microsoft.Extensions.Http.Resilience` (побудований на Polly) декларативно додає їх до `IHttpClientFactory` ([розділ 5](#ihttpclientfactory-resilience-with-polly)); у [розділі 20](#a-concrete-net-resilience-pipeline-with-polly) зібрано повний pipeline і пояснено, як поєднуються стратегії.

## Зловживання, боти й небажаний трафік

Обмежувач частоти запитів у попередньому підрозділі розрахований на *доброзичливий* світ: клієнт із циклом повторів, застосунок телефону, який опитує надто часто, або інтеграція партнера, що неправильно зрозуміла документацію. Ви встановлюєте ліміт, повертаєте `429`, клієнт зменшує частоту — і всі задоволені.

У цьому підрозділі йдеться про інший випадок: клієнт не збирається зменшувати частоту, має більше IP-адрес, ніж ви, і аналізує ваші відповіді, щоб визначити обмеження. Зовні засоби захисту схожі, але підхід до проєктування зовсім інший.

### Трафік змінився

Якщо давно не переглядали журнали, їхній склад може здивувати: у загальнодоступному інтернеті автоматизований трафік тепер становить приблизно половину всіх запитів, а частка пов'язана зі ШІ зростає. Це сканери, які збирають навчальні корпуси, пошукові роботи для отримання сторінок на запит користувача й агенти, що переглядають сайти від його імені. На сайтах документації, каталогів продуктів або великих текстових корпусів такі боти часто багаторазово запитують кожну сторінку й не дотримуються правил кешування браузера.

У результаті виникла справді нова операційна проблема: **витрати й використання потужностей зростають без атаки**. Ніхто не намагається вам нашкодити. Джерело перевантажене, рахунок за вихідний трафік зріс, база даних обробляє запити після промахів кешу для сторінок, які люди не читали вже рік, і звинуватити нікого.

**`robots.txt` — це прохання, а не засіб контролю.** Це угода, якої добровільно дотримуються сумлінні пошукові роботи. Дотримання правил ботами, пов'язаними зі ШІ, непослідовне: одні їх виконують, інші лише для пошукового робота, якого ви назвали, а не для засобу отримання даних, і дехто ігнорує повністю. Опублікувати `robots.txt` варто, але цього недостатньо.

**Блокування за user-agent майже не краще.** User-agent — це рядок, який клієнт обирає сам. Він корисний для *ідентифікації сумлінних* ботів і непотрібний проти тих, хто не хоче бути впізнаним.

Ось засоби, які справді працюють, від найнадійніших:

- **Перевірена ідентичність ботів, які її підтримують.** Основні пошукові роботи публікують діапазони IP-адрес або процедуру перевірки зворотного DNS: перетворіть IP-адресу клієнта на ім'я хоста, переконайтеся, що воно належить його домену, а потім перетворіть ім'я назад на IP. Так можна *надійно дозволити* потрібні боти — пошукові системи, що приводять користувачів, — а всі інші заяви без доказів вважати невідомими.
- **Поведінкові сигнали**, які важко підробити, бо це властивості самого трафіку, а не заяви клієнта: частота запитів із джерела, ширина охоплених URL (людина читає кілька сторінок, бот проходить мапу сайту), відсутність запитів ресурсів (бот отримує HTML, але пропускає CSS, шрифти й зображення, які завантажив би браузер), форма сеансу й ігнорування заголовків кешування.
- **Асиметрія вартості.** Зробіть дорогі операції дешевими для себе й дорогими для клієнта: віддавайте невідомим клієнтам агресивно кешовані відповіді через CDN, щоб джерело не торкалося запиту, а динамічне формування на основі бази даних залиште автентифікованим сеансам.
- **Доведення виконання роботи або проміжні сторінки перевірки** для невідомих клієнтів. Це змінює економіку: справжньому користувачу перевірка коштує кілька секунд, а сканеру — процесорного часу для кожної з мільйонів сторінок.

> **Рекомендація.** Перед вибором інструменту визначте *політику*: яких ботів ви хочете бачити (пошукові системи, що приводять трафік), до яких вам байдуже, а які лише збільшують витрати. Потім застосуйте політику на CDN, а не в застосунку. Джерело не обробляє лише той запит, який до нього не дійшов.

### DDoS-атаки за рівнями

Фраза «на нас була DDoS-атака» описує два різні випадки, і відмінність визначає, хто може щось зробити.

**Об'ємні атаки й атаки протокольного рівня (L3–L4)** перевантажують канал пропускання або вичерпують стан з'єднань: потоки UDP, SYN-потоки, посилення через DNS- чи NTP-відповідачі. Визначальна властивість — трафік не доходить до застосунку, а іноді й до вашої мережі: спершу заповнюється канал. **У коді цього не виправити.** Атаку поглинають вище за течією засоби фільтрації постачальника (AWS Shield, Azure DDoS Protection, Cloudflare тощо). Ваша робота — підготуватися заздалегідь: розміщуватися за таким сервісом, знати, що входить до вибраного рівня, і мати контакт для звернення.

**Атаки прикладного рівня (L7)** надсилають схожі на легітимні, але навмисно дорогі запити: пошук із патологічними параметрами, створення звіту, вхід у систему чи URL, що оминає кеш. Обсяг може бути помірним: кількох тисяч *правильних* запитів на секунду достатньо, щоб зупинити службу, яка обробляє сотні тисяч інших. Це вже *ваша* проблема; саме їй присвячено решту підрозділу.

> **Нюанс.** Найпоширеніший самостійно створений підсилювач L7 — ключ кешу з даними, які клієнт може довільно змінювати: параметром відстеження, випадковим параметром скидання кешу або заголовком, який ви варіювали. Кожен запит стає промахом, а CDN справно передає всі запити до джерела. Нормалізуйте ключі кешу й видаляйте невідомі параметри запиту на периферії.

### Rate limiting проти того, хто намагається його обійти

Три рішення відрізняють обмежувач, який заважає зловмиснику, від такого, що лише заважає вашим користувачам.

**Ключ обмеження визначає все.** Це компроміс між тим, як легко зловмиснику його обійти, та супутніми збитками:

| Ключ | Як обходить зловмисник | Супутні збитки |
|---|---|---|
| IP-адреса | Ботнет, пул проксі або IPv6 (один клієнт може мати префікс /64 — мільярди адрес) | Високі: корпоративний NAT, університетські мережі й CGNAT мобільних операторів розміщують тисячі справжніх користувачів за однією IP-адресою |
| Ключ API / обліковий запис | Реєстрація нових облікових записів | Низькі, але обмежуються лише автентифіковані запити |
| Орендар | — | Низькі; правильна одиниця для B2B-продукту, що захищає орендарів один від одного |
| Відбиток пристрою або сеансу | Очищення стану (дешево) | Помірні |

Практичне рішення складається з кількох шарів: щедре обмеження за IP як грубий резерв, справжнє обмеження для облікового запису чи орендаря як основний контроль і, що особливо важливо, для endpoints без автентифікації — обмеження IPv6 за префіксом **/64**, а не окремою адресою. Обмеження за IP-адресою IPv6 майже нічого не обмежує.

**Місце зберігання лічильника визначає, чи працює обмежувач.** Вбудований обмежувач частоти ASP.NET Core зберігає стан **у процесі**. Якщо за балансувальником десять реплік, політика «100 запитів на хвилину» насправді дозволяє до 1 000 запитів за хвилину та скидається під час повторного створення модуля. Зловмисник це виявить. Обмеження в процесі добре захищає *окремий екземпляр* від перевантаження, але не є системною політикою. Для цього потрібен спільний лічильник — Redis або обмежувач шлюзу API/CDN. Що далі від вашої служби ви розмістите контроль, то менше атаки дістанеться оплачуваної інфраструктури.

```
  attacker ──► [ CDN / WAF ]  ← cheapest place to say no; attack never costs you
                    │
                    ▼
              [ API gateway ]  ← shared counters, per-key policy
                    │
                    ▼
              [ your service ] ← in-process limiter as self-protection only
```

**Алгоритм має відповідати звичайному шаблону використання.** Фіксовані вікна найпростіші, але мають помітну зловмиснику ваду на межі: клієнт може використати весь ліміт о 11:59:59 і ще раз о 12:00:00, подвоївши потрібну частоту. Ковзні вікна виправляють це ціною додаткового стану. Для API зазвичай краще підходить відерце токенів, оскільки реальні клієнти працюють сплесками: запас токенів, який постійно поповнюється, допускає дванадцять запитів під час завантаження сторінки, не дозволяючи тривалий потік. А **обмеження паралельності** недооцінюють: для дорогих endpoints правило «одночасно працює не більш як N таких запитів» значно краще захищає ресурс, ніж rate limiting, бо прямо обмежує кількість ресурсомісткої роботи.

> **Пастка.** Не розкривайте ліміти у відповідях про помилки. Відповідь `429` цілком доречна. Але тіло `429` із точним поясненням політики й заголовками зі зворотним відліком квоти передає зловмиснику параметри для налаштування скрипту. Публікуйте ліміти в документації для сумлінних інтеграторів, а не пояснюйте їх у кожній відповіді неавтентифікованим клієнтам.

### Підстановка credentials і захоплення облікових записів

Витік даних в іншій службі стає вашим інцидентом. Зловмисники беруть набір викрадених адрес електронної пошти й паролів і пробують їх на вашій endpoint входу, розраховуючи на повторне використання паролів. Навіть частка успішних входів менша за відсоток серед мільйонів спроб забезпечує прибутковий день.

Від грубої атаки це відрізняється формою: з багатьох IP-адрес надходить **одна чи дві спроби для кожного облікового запису, але для величезної їх кількості**. Класичний захист — блокування окремого облікового запису — майже не спрацьовує, бо жодну адресу не атакують двічі.

Захист має відповідати формі атаки:

- **Перевіряйте паролі за базами витоків** під час реєстрації та зміни пароля (API Have I Been Pwned дає це зробити, жодного разу не надсилаючи пароль: передаються лише перші п'ять символів хешу SHA-1, а отримані суфікси шукаються локально). Це позбавляє атаку сенсу для ваших користувачів.
- **Ключі доступу / WebAuthn**, які не мають спільного секрету для підстановки ([розділ 12](#passkeys-webauthn-fido2)). Це справжнє виправлення, і тепер воно практичне.
- **Обмежуйте загальну частку невдалих входів endpoint**, а не лише спроби для кожного облікового запису. Раптове зростання відношення невдалих входів до успішних — сигнал, який помітний, навіть якщо для кожного облікового запису окремо немає проблем.
- **Додавайте складнощі залежно від ризику** — перевірку або другий чинник, коли запит надходить із невідомого пристрою, незвичного місця або джерела, яке вже має багато невдалих спроб. Не вводьте однакові перешкоди для всіх, бо користувачі звикнуть їх оминати.

> **Нюанс — блокування може стати атакою на відмову в обслуговуванні.** Правило «п'ять невдалих спроб блокують обліковий запис» дозволяє будь-кому, хто знає електронну адресу користувача, заблокувати йому доступ. Якщо блокування необхідне, блокуйте *джерело спроб*, а не обліковий запис; використовуйте експоненційне збільшення затримки замість жорсткого блокування та переконайтеся, що процес відновлення сам не став простішим шляхом атаки.

### Вичерпання грошей

Еластична інфраструктура змінила мету атак. Проти сервера з фіксованою потужністю перемога зловмисника — зупинити його. Для сервера з автоматичним масштабуванням служба лишається доступною, а *ви оплачуєте атаку*. Сповіщення не спрацьовують, бо нічого не зламалося. Потрібний графік лежить на фінансовій панелі, яку ніхто не переглядає щогодини.

Атака приносить прибуток на endpoints, де малий запит запускає багато роботи:

- **Endpoints LLM**, де один спеціально сформований запит спричиняє тривалий пошук, великий контекст і багатокроковий цикл агента — долари за запит. Саме тому [розділ 33](#chapter-33-building-ai-powered-systems) вважає необмежене споживання окремим критичним ризиком.
- **Пошук і створення звітів**, де патологічний запит сканує все.
- **Експорт і завантаження**, які безпосередньо збільшують вартість вихідного трафіку.
- **Обробка зображень і документів**, де невелике завантаження потребує хвилин роботи ЦП.
- **Будь-яка операція, яка** розсилає запити до платних сторонніх API з вашого облікового запису.

Захист не дивовижний, але має бути до запуску, а не після отримання рахунка: жорсткі квоти для користувачів і орендарів саме на дорогі операції (загальний ліміт API для них не підходить), обмежений бюджет вартості одного запиту, обмеження розміру й складності запитів (зокрема глибини запиту, якщо відкрито GraphQL) і **сповіщення про швидкість витрат, а не загальну суму**. Місячне сповіщення про бюджет розповість про вчорашні витрати аж через чотири тижні. [У розділі 31](#part-b-cloud-cost-finops) розглянуто керування витратами.

### Навмисне скидання навантаження

Коли потужності не вистачає через атаку, запуск або сповільнення залежності, різницю між важкою годиною й простоєм визначає те, чи вирішили ви заздалегідь, від чого відмовитися.

Типова поведінка — найгірша: кожен запит приймається й стає в queue, потім завершується тайм-аутом, а тим часом обслуговування немає, хоча робота все одно виконана. За перевантаження **рання відмова — це послуга, а не збій.** Швидко поверніть `429` або `503` із `Retry-After`, а не приймайте роботу, яку не зможете завершити.

Заздалегідь визначте порядок пріоритетів — о третій ночі зробити це добре не вдасться: автентифіковані запити перед анонімними, платні орендарі перед безкоштовними, оформлення замовлення перед переглядом каталогу, записи перед аналітикою. Реалізуйте порядок як політику queue або обмеження паралельності для кожного класу трафіку й — про це команди забувають — **перевірте під навантаженням знижений режим роботи**. Відмовостійкість, яку ніхто не перевіряв, — лише гіпотеза. Перетворити її на факт допоможе [внесення збоїв із розділу 20](#verifying-resilience-chaos-engineering-in-practice).

## Інженерія платформ і вимірювання доставки

Усе попереднє в цьому розділі — механізми: pipelines, артефакти, контрольні точки, секрети, кластери й проксі. Тепер ідеться про два запитання, які стосуються всіх механізмів і постають перед досвідченими інженерами, а не pipelines: **хто створює й підтримує це для всіх?** і **як дізнатися, чи воно працює?**

### Проблема, для якої існує інженерія платформ

Принцип «ви створюєте — ви підтримуєте» був реакцією на реальну проблему: розробники перекидали код через стіну команді експлуатації, яка нічого про нього не знала й не могла відмовити. Це спрацювало. А потім процес продовжився, і зрештою від серверного розробника стали очікувати, що він одночасно володіє Terraform, Kubernetes, Helm, service mesh, трьома продуктами observability, аналізатором IaC, менеджером секретів, двома моделями IAM хмари й черговою мовою CI — і водночас випускає функції.

Це проблема **когнітивного навантаження**, і найм більшої кількості досвідчених людей її не розв'яже. Коли організація сягає певного розміру, кожна команда окремо розв'язує ті самі проблеми інфраструктури дванадцятьма трохи різними й трохи неправильними способами. За це постійно платять супроводом та інцидентами.

**Інженерія платформ** — це відповідь: невелика команда створює й підтримує внутрішній продукт, користувачами якого є інші інженери. Слово *продукт* тут визначальне: воно означає, що з користувачами можна поговорити, довіру й використання треба заслужити, план робіт має визначати попит і є ризик створити не те.

| | DevOps (практика) | SRE | Інженерія платформ |
|---|---|---|---|
| Основна ідея | Розробка й експлуатація спільно відповідають за результат | Надійність як інженерна дисципліна | Інфраструктурні можливості як внутрішній продукт |
| Основний результат | Культура, автоматизація, цикли зворотного зв'язку | SLO, бюджети помилок, зменшення рутинної роботи | Зручні стандартні шляхи, самообслуговування |
| Коли не працює | Практика стає назвою посади однієї команди | Бюджети помилок мають лише рекомендаційний характер | Команда платформи стає queue завдань |

Ці підходи доповнюють, а не замінюють один одного. SRE дає словник надійності ([розділ 25](#alerting-slis-slos-slas-and-error-budgets)), а інженерія платформ — засоби послідовно його застосовувати.

### Золоті шляхи: викладена доріжка краща за контрольні ворота

**Золотий шлях** — рекомендований спосіб виконати типове завдання: створити службу, додати базу даних, відкрити endpoint, випустити зміни в продакшен. Це не *єдиний* спосіб — і ця відмінність надзвичайно важлива. Це шлях, для якого вже все підготовлено.

Якісний золотий шлях для нової служби .NET за однією командою створює репозиторій зі структурою проєкту й налаштуваннями аналізаторів компанії, готовим pipeline CI, контейнеризацією, перевірками стану й налаштованим OpenTelemetry, записом у каталозі служб, панеллю моніторингу, графіком чергування й deployment у середовищі розробки. Те, на що вмілому інженеру раніше йшло два тижні копіювання з сусіднього репозиторію, тепер займає день. А головна перевага — двадцята служба налаштована так само, як перша.

Принцип, від якого залежить успіх:

> **Рекомендація — прокладайте доріжку, а не ставте ворота.** Зробіть підтримуваний шлях очевидно простішим за альтернативи, і ним користуватимуться. Щойно основним механізмом платформи стає *відмова*, інженери починають його обходити, а ви створили бюрократію з графіком чергування.

Це не означає, що не потрібні обмеження. Вони мають бути *типовими налаштуваннями*, а не *погодженнями*: шаблон уже має правильну область IAM, базовий образ уже захищений, pipeline уже виконує перевірки безпеки. Жорстко забороняйте лише те, чого справді ніколи не можна робити (передавати в продакшен непідписаний образ, додавати секрет до коміту), а все інше задавайте за замовчуванням, дозволяючи команді відступити від правила з письмовим обґрунтуванням.

**Золоті шляхи занепадають.** Шаблон, створений рік тому, — це знімок; сто створених на його основі служб поступово перетворюються на сто варіантів. Закладайте ресурси на поширення змін — інструмент, який оновлює шаблони в наявних репозиторіях і відкриває PR. Або прийміть, що золотий шлях стосується лише нових служб, і його користь буде значно меншою, ніж здавалося.

### Каталоги служб і відповідальність

Найцінніше у внутрішній платформі — не інструменти, а відповідь на запитання *«хто це підтримує?»*. У кожній організації, де понад тридцять служб, є компонент, за який ніхто не може впевнено взяти відповідальність, і зазвичай він критично важливий.

**Backstage** (проєкт CNCF, що виник у Spotify) — поширена реалізація з open source (відкритий код) і серйозне зобов'язання: це застосунок Node, який команда підтримує й до якого розробляє модулі. Є й комерційні альтернативи. Перш ніж обрати будь-який варіант, зрозумійте, що робить каталог корисним: справа не в програмному забезпеченні.

- Дані про власників **актуальні** — це забезпечується CI (для запуску pipeline потрібен `CODEOWNERS` або запис у каталозі), а не доброю волею.
- Дані **генеруються** з того, що вже є (репозиторії, deployment, панелі), а не вводяться вручну.
- Люди справді **відкривають каталог під час роботи**: зі сповіщення, графа залежностей або коли думають, кого запитати.

Каталог, до якого ніхто не звертається, бо його дані застаріли на дев'ять місяців, гірший за відсутність каталогу: він дає впевнені, але неправильні відповіді.

### DORA: чотири метрики й способи маніпулювати кожною

Програма дослідження DORA визначила чотири показники, пов'язані з ефективністю доставки програмного забезпечення. Це спільна мова галузі; важливіше розуміти, як кожен показник ламається, ніж просто знати його визначення.

| Метрика | Що вимірює | Як нею маніпулюють |
|---|---|---|
| **Частота розгортань** | Як часто випуски потрапляють у продакшен | Повторно розгортати той самий артефакт, рахувати порожні deployment або змінити визначення слова «deployment» |
| **Час від зміни до випуску** | Від коміту до роботи в продакшені | Починати відлік із відкриття PR, а не першого коміту, приховуючи тижні попередньої роботи |
| **Частота невдалих змін** | Частка розгортань, що спричинили погіршення роботи | Перекласифікувати інциденти як «планові роботи» або підвищити поріг того, що вважається збоєм |
| **Час відновлення після невдалого deployment** | Час до відновлення роботи служби | Закривати інцидент після тимчасового усунення, а не після остаточного виправлення; розбивати один інцидент на кілька коротких |

Два структурні застереження.

**Вимірюється пропускна здатність і стабільність, а не цінність.** Команда може досягти найкращих показників за всіма чотирма метриками й випускати функції, якими ніхто не користується. DORA вимірює роботу системи доставки, а не корисність результату. Метрики не призначалися для оцінки цінності — це найпоширеніше хибне тлумачення.

**Вимірювання втрачає сенс, щойно стає ціллю.** Це закон Ґудгарта, якого не уникнути, обравши кращі метрики. Зменшити проблему можна, використовуючи показники для *власної діагностики команди*, відстежуючи зміни в часі й обговорюючи на ретроспективі. Важливо **не порівнювати команди між собою й не прив'язувати показники до оцінювання працівників**. Щойно час до випуску з'явиться на панелі менеджера поруч із прізвищами, вимірюватиметься поведінка звітування.

> **Нюанс.** Частота невдалих змін і частота розгортань — *пара*. Поліпшення однієї за рахунок іншої не є поліпшенням. Якщо дивитися лише на одну, це заохочує неправильну поведінку — безрозсудні випуски або параліч. Завжди оцінюйте їх разом.

### SPACE: коригувальна методика

Методику SPACE запропонували дослідники (серед них деякі автори DORA) саме тому, що метрики одного виміру спотворюють оцінку. Вона наголошує: продуктивність багатовимірна, тому замість оптимізації одного показника потрібно вибірково вимірювати п'ять вимірів:

- **S**atisfaction — задоволеність і добробут: чи подобаються розробникам їхні інструменти й робота? Виснаження передує звільненням, які шкодять доставці.
- **P**erformance — результати: чи спрацювала зміна, чи збереглася якість?
- **A**ctivity — кількість виконаного. Це необхідний показник, але окремо він найбільш оманливий.
- **C**ommunication and collaboration — спілкування й співпраця: час перегляду, доступність інформації, рух знань.
- **E**fficiency and flow — ефективність і потік роботи: безперервний час, очікування, передавання між учасниками.

Практична порада: оберіть **щонайменше три виміри, зокрема один на основі опитування**, і ніколи не звітуйте лише про активність. Опитування розробників — не «м'які дані»: часто лише вони виявляють справжню перешкоду в роботі команди. Власні дослідження DORA постійно показують, що головні обмеження є організаційними, а не технічними.

### Чи допомагає ШІ в роботі

Це актуальний приклад проблеми вимірювання і момент, коли описана вище дисципліна стає важливою. Докази неоднозначні: у рандомізованому дослідженні 2025 року досвідчені розробники, які працювали зі знайомими кодовими базами, *працювали повільніше* з допомогою ШІ, хоча вважали, що значно прискорилися. Відчуття швидкості — не доказ.

Як чесно вимірювати результат, які метрики вводять в оману (рядки коду, відсоток коду від ШІ, кількість PR), які допомагають (тривалість циклу разом із частотою невдалих змін, затримка перегляду як випереджальний показник, витрати токенів на об'єднаний PR) і чому відповідь залежить від знайомства з кодовою базою — розглянуто в [розділі 32](#chapter-32-the-ai-native-developer-thriving-in-the-ai-era). Головний висновок: **допомога ШІ переносить bottleneck (вузьке місце) з написання коду на його перевірку**. Якщо PR надходять швидше, а затримка перегляду зростає, пропускну здатність не збільшено — лише виросла queue.

### Час циклу зворотного зв'язку — першорядне інженерне завдання

Найнепомітніше, але найвигідніше завдання команди платформи — скорочувати цикл. Розробник, який чекає на CI 25 хвилин, не просто чекає: він перемикається на іншу роботу, а ціна перемикання значно перевищує сам час CI. Набір тестів, надто повільний для локального запуску, зрештою перестає виявляти проблеми.

На що зазвичай витрачається час і де найкраща віддача:

- **Кешуйте детерміновані операції.** Відновлення NuGet із ключем за `packages.lock.json` (див. вище *Кешування допомагає лише за детермінованого відновлення пакетів*), шари Docker у порядку, що не дає зміні джерельного коду скидати шари залежностей, і результати складання.
- **Виконуйте паралельно.** Незалежні завдання не мають бути послідовними етапами. xUnit за замовчуванням запускає колекції тестів паралельно; перевірте, чи не вимкнено це спільною фікстурою.
- **Запускайте потрібну підмножину для потрібної events.** Модульні тести — після кожного надсилання змін; інтеграційні й наскрізні — для PR; повна матриця — щоночі. Вибір проєктів за зміненими шляхами особливо ефективний у рішенні з багатьма проєктами.
- **Доберіть виконавця за розміром.** Якщо складання обмежене ЦП на виконавці з двома ядрами, рішення про витрати на потужніший виконавець просте: година інженерної роботи коштує дорожче за обчислення.
- **Вимірюйте.** Відстежуйте p50 і p95 тривалості pipeline як метрики, на які справді дивиться команда, так само, як затримку служби. CI безшумно погіршується, поки хтось не побудує графік.

**Монорепозиторій чи багато репозиторіїв** впливає на все це. Монорепозиторій дає атомарні зміни між службами, одну версію залежностей і узгоджений інструментарій, але для швидкої роботи потрібні вибір цільових проєктів за зміненими файлами й чіткі межі відповідальності. Багато репозиторіїв забезпечують незалежність і простий CI, але ускладнюють координацію змін між межами й змушують версіонувати внутрішні бібліотеки, наче вони публічні. Обидва підходи працюють у великому масштабі; не працює монорепозиторій без інструментів графа складання чи багато репозиторіїв без засобу поширювати зміни між сорока репозиторіями. Оберіть той режим відмови, з яким можете впоратися інженерними засобами.

