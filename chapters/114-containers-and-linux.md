# Chapter 14: Containers and Linux

The default target for a cloud deployment is a Linux container, and "it works on my machine" fails there in specific, explainable ways: a file the app can't write, a shutdown that loses requests, a port nobody can reach.

The chapter has one through-line: **a container is an ordinary Linux process** with a restricted view of the system (namespaces) and a capped share of it (cgroups), started from a layered, read-only image. Everything that goes wrong inside one is a Linux behaviour: file permissions decide what it can write, signals decide whether it shuts down gracefully, environment variables carry its configuration, and stdout carries its logs.

Running containers at scale (Kubernetes, Helm, service meshes, .NET Aspire) and running .NET under systemd continue in [Chapter 26: Delivery and Platform](#chapter-26-delivery-and-platform).

## Why Linux Matters for Modern .NET

Three forces pushed .NET onto Linux.

**Containers.** Docker images are almost always built `FROM` a Linux base. Microsoft ships `mcr.microsoft.com/dotnet/aspnet:10.0` as a Debian-based Linux image by default, with Alpine and Ubuntu variants. When you `docker build` and `docker run`, you are running your app on a tiny Linux system, even if your laptop is Windows. Kubernetes — the industry standard orchestrator — schedules Linux containers.

**Cost.** Linux hosting is cheaper. There is no per-core Windows Server licensing, images are smaller, and startup is faster. A company running thousands of container replicas saves real money by targeting Linux. That decision is made above you, and it lands on your desk as "the app must run on Linux."

**The cloud is Linux underneath.** Azure App Service, AWS, and Google Cloud all run enormous Linux fleets. Managed services, CI runners (GitHub Actions `ubuntu-latest`, Azure Pipelines Linux agents), and serverless functions default to Linux.

> **Best practice:** Develop and test on the same OS family you deploy to. A subtle but real class of bugs comes from Linux being **case-sensitive** for file paths while Windows is not. `Views/Home.cshtml` and `views/home.cshtml` are the same file on Windows and two different (missing) files on Linux. Catch these before production.

## The Shell: Your Real Interface

When people say "the command line" on Linux they usually mean a **shell** — a program that reads text commands, runs them, and shows output. The two you'll meet are **bash** (the Bourne Again Shell, the long-standing default) and **zsh** (the default on macOS and increasingly popular on Linux). For everything in this chapter they behave almost identically; differences only matter for advanced scripting.

The shell's core job is simple: read a line, split it into a command and arguments, find the program, run it, and wait. When you type `ls -l /var`, the shell finds the `ls` executable (by searching the directories in your `PATH` environment variable), hands it the arguments `-l` and `/var`, and runs it.

A few things the shell does *before* the program ever sees your input, which trip people up:

- **Globbing:** `*.dll` is expanded by the shell into a list of matching filenames before the program runs. The program never sees the `*`.
- **Variable expansion:** `$HOME` becomes `/home/you`.
- **Quoting:** single quotes `'...'` are literal; double quotes `"..."` allow variable expansion. Wrap paths with spaces in quotes.

```bash
# The shell expands the glob; 'ls' receives the actual filenames
ls -l *.dll

# Single quotes stop expansion — useful when you want a literal $
echo 'The cost is $5'      # prints: The cost is $5
echo "Home is $HOME"       # prints: Home is /home/you
```

## The Filesystem Hierarchy

Windows gives you drive letters (`C:\`). Linux has a single tree rooted at `/`, and everything — every disk, every device — hangs off it. The layout follows a convention (the Filesystem Hierarchy Standard). The directories you actually care about as a .NET developer:

| Path | What lives there | Why you care |
|------|------------------|--------------|
| `/etc` | System-wide configuration | systemd unit files, nginx config live here |
| `/var` | Variable data: logs, caches | `/var/log` is where logs go; `/var/lib` for state |
| `/usr` | Installed programs and libraries | `/usr/bin/dotnet`, shared libs |
| `/home/<user>` | Per-user home directories | Your `~`, config, SSH keys |
| `/tmp` | Temporary files, wiped on reboot | Scratch space; often the *only* writable dir in a locked-down container |
| `/proc` | Virtual filesystem exposing kernel/process state | `/proc/<pid>/status` shows a process's memory; not real files |
| `/opt` | Optional/third-party software | Some vendors drop apps here |

Paths are separated by `/`, not `\`. `~` is shorthand for your home directory. `.` means the current directory and `..` means the parent. A path starting with `/` is **absolute**; anything else is **relative** to where you currently are.

```bash
pwd                 # print working directory — where am I?
cd /var/log         # go to an absolute path
cd ..               # up one level
cd ~                # go home (cd with no args does the same)
cd -                # jump back to the previous directory
```

## Essential Commands

```bash
ls -lah                 # list: long format, all files, human-readable sizes
cp source.txt dest.txt  # copy
cp -r src/ dst/         # copy a directory recursively
mv old.txt new.txt      # move or rename
rm file.txt             # remove a file
rm -rf node_modules/    # remove a directory tree — DANGEROUS, no undo
mkdir -p a/b/c          # create nested directories
```

> **Pitfall:** `rm -rf` has no recycle bin. `rm -rf /` or a stray variable like `rm -rf $DIR/` where `$DIR` is empty (expanding to `rm -rf /`) can wipe a system. Double-check the path. Always.

**Finding things:**

```bash
find /app -name "*.log"              # find files by name pattern
find /app -type f -mtime +7          # files modified more than 7 days ago
find . -name "*.dll" -delete         # find and delete matches

grep "ERROR" app.log                 # lines containing ERROR
grep -r "ConnectionString" .         # recursive search through a tree
grep -i -n "timeout" app.log         # case-insensitive, with line numbers
```

**Reading files and logs:**

```bash
cat appsettings.json     # dump a whole file to the screen
less app.log             # page through a big file (arrows, /search, q to quit)
head -n 20 app.log       # first 20 lines
tail -n 100 app.log      # last 100 lines
tail -f app.log          # follow — stream new lines live, great for logs
```

`tail -f` is your friend when watching an app write logs in real time.

**Stream editing with sed and awk** — you don't need mastery, just survival:

```bash
sed 's/localhost/prod-db/g' config.txt   # substitute all localhost -> prod-db
awk '{print $1}' access.log               # print the first whitespace field
awk -F: '{print $1}' /etc/passwd          # split on ':' , print field 1
```

`sed` does find-and-replace on streams; `awk` slices columnar text. Together with `grep` they let you carve information out of logs without leaving the terminal.

**Talking to the network:**

```bash
curl https://api.example.com/health           # fetch a URL, print the body
curl -i http://localhost:5000/health          # include response headers
curl -X POST -H "Content-Type: application/json" \
     -d '{"name":"test"}' http://localhost:5000/api/items

ssh appuser@10.0.0.5                           # open a remote shell
scp app.zip appuser@10.0.0.5:/tmp/             # copy a file to a remote host
```

## Pipes, Redirection, Exit Codes, and Chaining

This is where the shell becomes a programming environment. Every program has three streams: **stdin** (0), **stdout** (1), and **stderr** (2).

**Redirection** sends those streams to files:

```bash
dotnet MyApp.dll > out.log             # stdout to a file (overwrite)
dotnet MyApp.dll >> out.log            # stdout appended
dotnet MyApp.dll 2> err.log            # stderr to a file
dotnet MyApp.dll > all.log 2>&1        # both stdout and stderr to one file
dotnet MyApp.dll < input.txt           # feed a file as stdin
```

**Pipes** (`|`) connect one program's stdout to the next program's stdin. This is the Unix philosophy — small tools composed into pipelines:

```bash
# Show the 5 lines with the most ERRORs across today's logs
grep "ERROR" app.log | sort | uniq -c | sort -rn | head -5

# Which dotnet processes are eating memory?
ps aux | grep dotnet | sort -k4 -rn | head
```

**Exit codes** are how programs report success. By convention, `0` means success and anything non-zero means failure. The shell stores the last exit code in `$?`. This is exactly what CI pipelines check to decide pass/fail — and what `dotnet test` returns.

```bash
dotnet build
echo $?          # 0 if the build succeeded, non-zero if it failed
```

**Chaining** uses exit codes to control flow:

```bash
dotnet build && dotnet test          # run tests ONLY if build succeeded
dotnet test || echo "tests failed"   # run the echo ONLY if tests failed
dotnet restore ; dotnet build        # run both regardless (';' just sequences)
```

`&&` = "and then, if the previous succeeded." `||` = "or else, if it failed." You'll write these constantly in Dockerfiles and CI scripts.

## Permissions: Why Your Container App Can't Write That File

Every file and directory has an **owner** (a user), a **group**, and a set of permission bits for three classes: the owner (`u`), the group (`g`), and everyone else (`o`). Each class gets three bits: **read (r)**, **write (w)**, and **execute (x)**. Run `ls -l` and read the first column:

```bash
ls -l app.dll
# -rw-r--r-- 1 appuser appgroup 8192 Jul 21 10:00 app.dll
```

Decode `-rw-r--r--`:

- First char `-` = regular file (`d` would mean directory).
- `rw-` = owner can read and write, not execute.
- `r--` = group can read only.
- `r--` = others can read only.

For a **directory**, `x` means "can enter/traverse it," and `w` means "can create or delete files inside it." This is why an app can fail to write a log file even when it owns the log file — it may lack `w` on the *directory*.

Permissions are also written as octal, where `r=4, w=2, x=1`. So `rwxr-xr-x` = `755`, and `rw-r--r--` = `644`.

```bash
chmod 644 appsettings.json     # owner rw, everyone else r
chmod +x deploy.sh             # add execute so you can run the script
chmod 755 /app/data            # make a dir traversable + writable by owner

chown appuser:appgroup app.dll         # change owner and group
chown -R appuser /app/logs             # recursive, for a whole tree
```

Now the classic container failure. Your Dockerfile switches to a non-root user for security (the `USER` instruction; *Containerizing a .NET Application* below builds the whole file):

```dockerfile
USER app        # runs as UID 1654, not root
```

Your app then tries to write to `/app/data`, but that directory was created earlier in the build as `root` with `755` — meaning only root can write. At runtime your process is UID 1654, gets **write denied**, and .NET throws `UnauthorizedAccessException`. The fix is to give ownership to the runtime user during the build:

```dockerfile
RUN mkdir -p /app/data && chown -R app:app /app/data
USER app
```

> **Pitfall:** Mounted volumes bring their *host* ownership into the container. A volume owned by host UID 0 mounted into a container running as UID 1654 will be unwritable no matter what your Dockerfile does. Match the UIDs, or set ownership on the volume, or run an init step as root that `chown`s the mount.

The official .NET 8+ images include that pre-created `app` user (UID 1654), and *Running as non-root* below explains why you keep it: don't fix a permission error by switching back to root.

## Processes & Signals: Graceful Shutdown

A running program is a **process** with a numeric **PID**. Inspect them:

```bash
ps aux                    # every process, with CPU/memory
ps aux | grep dotnet      # just the dotnet ones
top                       # live, updating process view (press q to quit)
htop                      # nicer top, if installed — colored, scrollable
```

Processes communicate via **signals** — small asynchronous notifications. The two you must know:

- **SIGTERM (15):** "Please shut down." The process can catch it, finish in-flight requests, flush logs, close DB connections, then exit. This is the **graceful** signal.
- **SIGKILL (9):** "Die now." Cannot be caught or ignored; the kernel terminates the process immediately. No cleanup. Data loss risk.

```bash
kill 4321          # sends SIGTERM by default — polite
kill -9 4321       # sends SIGKILL — the hammer, last resort
kill -TERM 4321    # explicit SIGTERM
pkill dotnet       # kill by name
```

This ties directly into containers. When Kubernetes or `docker stop` shuts down your app, it sends **SIGTERM**, waits a grace period (10 seconds by default for `docker stop`, set with `-t`; `terminationGracePeriodSeconds` in Kubernetes, 30 by default), and only then sends **SIGKILL**. ASP.NET Core's generic host listens for SIGTERM and triggers `IHostApplicationLifetime.ApplicationStopping`, runs your `IHostedService.StopAsync`, and drains requests. If your app ignores SIGTERM or takes too long, it gets SIGKILL'd mid-request. [Chapter 11: Graceful shutdown and the `CancellationToken`](#graceful-shutdown-and-the-cancellationtoken) covers the application's side: the shutdown timeout and the token every worker must observe.

> **Best practice:** Make sure .NET actually *receives* SIGTERM. Use the **exec form** of `ENTRYPOINT` (`ENTRYPOINT ["dotnet", "MyApp.dll"]`), not the shell form (`ENTRYPOINT dotnet MyApp.dll`). The shell form runs your app as a child of `/bin/sh`, which is PID 1 and does not forward signals — so SIGTERM never reaches .NET and every shutdown is a hard kill.

### Foreground, Background, and Jobs

A command normally runs in the **foreground**, tying up your terminal. Append `&` to run it in the **background**:

```bash
dotnet MyApp.dll &      # runs in background, prints a job number and PID
jobs                    # list background jobs in this shell
fg %1                   # bring job 1 back to the foreground
# Ctrl+Z suspends the foreground job; bg %1 resumes it in the background
```

For anything long-lived on a server you'd use systemd ([Chapter 26: systemd](#systemd-running-a-net-app-as-a-service)) or a container, not `&` — background jobs die when your shell exits.

## Environment Variables and How .NET Reads Them

Environment variables are key–value pairs the shell passes to programs. They're the primary way you configure a containerized app — no config file rebuild needed.

```bash
export ASPNETCORE_ENVIRONMENT=Production   # set for this shell and children
echo $ASPNETCORE_ENVIRONMENT               # read it back
printenv                                   # list all environment variables
NAME=value dotnet MyApp.dll                # set just for this one command
```

.NET's configuration system reads environment variables automatically, as one of its layered providers ([Chapter 3: The Configuration System](#the-configuration-system)). Two conventions matter:

- **`ASPNETCORE_` prefix** configures the ASP.NET Core host — e.g. `ASPNETCORE_ENVIRONMENT=Production`, `ASPNETCORE_URLS=http://+:8080`.
- **`DOTNET_` prefix** configures the runtime and generic host — e.g. `DOTNET_ENVIRONMENT`, `DOTNET_gcServer=1`.

Beyond prefixes, the config system maps **double underscores** (`__`) to the nested-section separator (`:`, which isn't legal in env var names on all platforms). So a JSON setting like:

```json
{ "ConnectionStrings": { "Default": "Server=..." } }
```

is overridden by:

```bash
export ConnectionStrings__Default="Server=prod;Database=app;..."
```

This is how you inject secrets and connection strings into containers without baking them into the image. In a Dockerfile or Kubernetes manifest you set `ConnectionStrings__Default` as an env var and .NET picks it up.

```bash
docker run -e ASPNETCORE_ENVIRONMENT=Production \
           -e ConnectionStrings__Default="Server=db;..." \
           -p 8080:8080 myapp:latest
```

> **Best practice:** Never commit secrets to `appsettings.json`. Use environment variables (or a secret store, [Chapter 12: Secrets Management](#secrets-management)) for anything sensitive. The `__` convention lets env vars cleanly override file-based config, which is exactly the precedence order .NET applies.

## Package Managers, Briefly

To install software you use the distro's package manager, not a website download. The two families:

```bash
# Debian / Ubuntu (apt)
sudo apt-get update              # refresh the package index first
sudo apt-get install -y curl     # install curl, no prompts

# RHEL / Fedora / Amazon Linux (yum/dnf)
sudo yum install -y curl
```

`sudo` runs a command as root (the administrator). You'll see `apt-get update && apt-get install` chained in Dockerfiles — the `update` refreshes the index, the `install` uses it. Always combine them in one `RUN` layer so you don't cache a stale index.

## Text Editors: nano and vim Survival

Sooner or later you'll SSH into a box with no GUI and need to edit a config file. Two editors are essentially always present.

**nano** is the friendly one. The commands are shown at the bottom of the screen; `^` means Ctrl.

```bash
nano appsettings.json
# Edit normally. Ctrl+O then Enter to save. Ctrl+X to exit.
```

**vim** is powerful but modal, which confuses newcomers. You need just enough to escape:

```bash
vim config.txt
# Press i to enter INSERT mode and type normally.
# Press Esc to return to NORMAL mode.
# Type :wq then Enter to write and quit.
# Type :q! then Enter to quit WITHOUT saving (when you've made a mess).
```

> **Pitfall:** If you find yourself trapped in vim with a keyboard full of beeps, press `Esc` then type `:q!` and Enter. That's the universal escape hatch. Learning this before you need it will save you real panic.

## WSL2: Developing on Windows, Targeting Linux

You don't have to abandon Windows to get comfortable on Linux. **WSL2** (Windows Subsystem for Linux, version 2) runs a real Linux kernel in a lightweight VM, integrated into Windows. You get a genuine Ubuntu (or other distro) shell, Docker Desktop backed by it, and full interop with your Windows files.

```powershell
wsl --install                 # install WSL2 with the default Ubuntu distro
wsl --list --verbose          # see installed distros and their version
wsl                           # drop into your Linux shell
```

For .NET work this is close to ideal: build and run your app inside WSL2's Linux so you catch case-sensitivity and permission issues *before* they hit production, while still using Visual Studio or VS Code on Windows. VS Code's WSL extension edits Linux files natively.

> **Best practice:** Keep your project files inside the WSL2 Linux filesystem (`~/projects/...`), not on the Windows drive (`/mnt/c/...`). Cross-filesystem access is dramatically slower, and file-watching (hot reload) is unreliable across the boundary.

## What a Container Actually Is

A container is a combination of things you have already seen: processes, a filesystem tree and a few kernel features. The single most common misconception is that a container is a lightweight virtual machine. It is not.

A **virtual machine** virtualizes *hardware*. A hypervisor (VMware, Hyper-V, KVM) presents fake CPUs, fake network cards, and fake disks to a **complete guest operating system**, kernel and all. If you run five VMs on a host, you are running five separate kernels, each consuming hundreds of megabytes of RAM before your application starts, each booting for tens of seconds.

A **container** virtualizes the *operating system*. There is no guest kernel. Every container on a host shares the **host's single Linux kernel**. What makes a container feel isolated — its own process list, its own network interfaces, its own root filesystem — is a set of kernel features, not a separate machine.

### The two kernel features that make it work

**Namespaces** provide *isolation* — they control what a process can *see*. Linux has several kinds, and a container is essentially a process placed inside a fresh set of them:

- **PID namespace** — the container's main process sees itself as PID 1, and cannot see host processes.
- **Network namespace** — the container gets its own network stack: interfaces, routing table, and ports. Two containers can both bind port 80 without conflict.
- **Mount namespace** — the container has its own view of the filesystem tree, rooted at the container image rather than the host's root.
- **UTS namespace** — its own hostname.
- **User namespace** — maps user IDs, so root *inside* the container can map to an unprivileged user *outside*.

**Control groups (cgroups)** provide *limits* — they control what a process can *use*. A cgroup caps CPU shares, memory, and I/O bandwidth for a group of processes. When you tell Kubernetes a pod may use "500m CPU and 512Mi memory," that limit is ultimately enforced by cgroups.

> **Pitfall:** Because containers share the host kernel, a Linux container cannot run natively on Windows or macOS. Docker Desktop quietly runs a lightweight Linux VM in the background and starts your containers *inside* it. On a Linux server there is no such VM — containers run directly on the host kernel.

### Images vs. Containers

These two words get used interchangeably in conversation, but they are precisely distinct:

- An **image** is a read-only template — a stack of filesystem layers plus metadata (the command to run, environment variables, exposed ports). It is inert, like a class definition or an installer.
- A **container** is a running (or stopped) *instance* of an image, with a thin writable layer on top. It is live, like an object instantiated from a class.

One image can spawn a thousand containers, just as one class can produce a thousand objects. When a container writes a file, it doesn't modify the image; the write lands in the container's own writable layer via **copy-on-write**. Delete the container and that writable layer vanishes — which is exactly why containers are called *ephemeral* and why persistent data must live in volumes, not inside the container.

## Docker: Building Images

Docker popularized containers by giving them an ergonomic build tool and a distribution format. You describe an image declaratively in a **Dockerfile**, and `docker build` executes it into an image.

### Layers and layer caching

Every instruction in a Dockerfile that changes the filesystem produces a new **layer** — a diff on top of the previous layer. Images are these layers stacked and stored by content hash. This layering drives Docker's most important performance characteristic: **the build cache**.

When Docker executes a build, for each instruction it checks whether it has already built an identical layer (same instruction, same input). If so, it reuses the cached layer and skips the work. The moment one instruction's input changes, that layer and **every layer after it** are invalidated and rebuilt.

This single rule dictates how you should order a Dockerfile: **put the things that change rarely near the top, and the things that change constantly near the bottom.** For a .NET app, your project files and restored NuGet packages change far less often than your source code. So you copy the `.csproj` and restore *first*, then copy the rest of the source. That way, editing a `.cs` file doesn't force a full package restore.

### Common Dockerfile instructions

- `FROM` — the base image to build on. Every Dockerfile starts here.
- `WORKDIR` — sets the working directory for subsequent instructions (and creates it).
- `COPY` / `ADD` — copy files from the build context into the image. Prefer `COPY`; `ADD` has surprising extra behavior (URL fetching, auto-extracting tarballs).
- `RUN` — execute a command at *build* time, producing a new layer (e.g. `dotnet restore`).
- `ENV` — set an environment variable baked into the image.
- `ARG` — a build-time variable, available only during the build.
- `EXPOSE` — documents which port the app listens on (metadata only; it doesn't actually publish the port).
- `USER` — sets the user for subsequent instructions and the running container.
- `ENTRYPOINT` / `CMD` — define what runs when the container starts. `ENTRYPOINT` is the fixed executable; `CMD` supplies default arguments.

### .dockerignore

The **build context** is the set of files Docker sends to the build engine before executing the Dockerfile. If you build from a folder containing `bin/`, `obj/`, `.git/`, and `node_modules/`, all of that gets shipped to the daemon — slowing the build and risking secrets or stale binaries leaking into your image. A `.dockerignore` file excludes them, exactly like `.gitignore`:

```gitignore
# .dockerignore
**/bin/
**/obj/
**/.vs/
**/.git/
**/node_modules/
**/*.user
Dockerfile
docker-compose*.yml
README.md
.env
```

> **Pitfall:** Without a `.dockerignore`, a stray `bin/Debug` folder copied by a broad `COPY . .` can shadow the freshly published output or bloat the context by hundreds of megabytes. Always add one.

### Multi-stage builds

Here is the tension: to *build* a .NET app you need the whole SDK (compilers, NuGet, MSBuild) — hundreds of megabytes. To *run* it you need only the much smaller runtime. A **multi-stage build** lets you use a fat SDK image to compile, then copy just the published output into a slim runtime image, discarding the SDK entirely. The final image contains none of the build tooling.

## Containerizing a .NET Application

```dockerfile
# syntax=docker/dockerfile:1

# ---- Stage 1: build & publish ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only the project files first so restore is cached
# when only source code changes.
COPY ["MyApi/MyApi.csproj", "MyApi/"]
COPY ["MyApi.Core/MyApi.Core.csproj", "MyApi.Core/"]
RUN dotnet restore "MyApi/MyApi.csproj"

# Now copy the rest of the source and publish.
COPY . .
WORKDIR /src/MyApi
RUN dotnet publish "MyApi.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ---- Stage 2: runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Copy only the published output from the build stage.
COPY --from=build /app/publish .

# Run as the built-in non-root user shipped in the base image.
USER $APP_UID

# Kestrel listens here; ASP.NET Core reads ASPNETCORE_HTTP_PORTS.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "MyApi.dll"]
```

A few details worth understanding:

- **`AS build` / `AS final`** name the stages. `COPY --from=build` reaches into the earlier stage to grab only `/app/publish`. The SDK never reaches the final image.
- **`--no-restore`** on publish avoids a redundant second restore, since we already restored in a cached layer.
- **`USER $APP_UID`** — modern Microsoft base images define a non-root user via the `APP_UID` environment variable (UID 1654). Running as this user is a critical security control (more below).
- **Port 8080, not 80** — since .NET 8, the default Microsoft images run as non-root, and non-root users cannot bind privileged ports (below 1024). The images default to 8080 for exactly this reason.

### Running as non-root

By default a container's process runs as **root** — root inside the container, which (absent user namespaces) is a genuine risk. If an attacker escapes the container through a kernel vulnerability, they land on the host with root privileges. Running as an unprivileged user shrinks that blast radius dramatically; the price is the ownership work from [Permissions](#permissions-why-your-container-app-cant-write-that-file) above, for every directory and volume the app writes to.

> **Best practice:** Never run production containers as root. Use `USER $APP_UID` with Microsoft's images, or create a dedicated user. Combine it at runtime with a read-only root filesystem and dropped Linux capabilities for defense in depth.

### Chiseled and distroless images: shrinking the attack surface

A standard `aspnet:10.0` image is based on Debian and includes a shell, a package manager, and dozens of system utilities. Your app needs almost none of them — but an attacker who breaks in can use them. **Chiseled** images (Microsoft's take on "distroless") strip the image down to the bare minimum: the .NET runtime and its direct dependencies, with **no shell, no package manager, and a non-root user by default.**

```dockerfile
# Runtime stage using an Ubuntu Chiseled image.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app/publish .
# Chiseled images already run as non-root (UID 1654) and default to port 8080.
ENTRYPOINT ["dotnet", "MyApi.dll"]
```

The payoffs are substantial: images are tens of megabytes smaller, there's a far smaller surface for CVEs, and non-root is the default. The trade-off is that you **cannot `docker exec` a shell into the container** to poke around — there is no `/bin/sh`. For debugging you attach an ephemeral debug container or rely on logs and metrics.

## Docker Compose for Local Development

A real application is rarely one process. Yours might need a Postgres database and a Redis cache alongside it. Starting each by hand — with the right ports, environment variables, and startup order — is tedious and error-prone. **Docker Compose** describes a multi-container stack in a single YAML file and brings it all up with one command.

```yaml
# docker-compose.yml
services:
  api:
    build:
      context: .
      dockerfile: MyApi/Dockerfile
    ports:
      - "8080:8080"          # host:container
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      ConnectionStrings__Postgres: "Host=db;Port=5432;Database=appdb;Username=app;Password=devsecret"
      ConnectionStrings__Redis: "cache:6379"
    depends_on:
      db:
        condition: service_healthy
      cache:
        condition: service_started

  db:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: appdb
      POSTGRES_USER: app
      POSTGRES_PASSWORD: devsecret
    ports:
      - "5432:5432"
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U app -d appdb"]
      interval: 5s
      timeout: 3s
      retries: 5

  cache:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    command: ["redis-server", "--save", "60", "1"]

volumes:
  pgdata:
```

Key concepts illustrated here:

- **Service discovery by name.** Compose puts all services on a shared network where each is reachable by its service name. The API connects to Postgres at host `db` and Redis at host `cache` — no IP addresses, no `localhost`. That's why the connection string says `Host=db`.
- **`depends_on` with `condition: service_healthy`** — Postgres takes a moment to accept connections after its container starts. The `healthcheck` runs `pg_isready` until the database is truly ready, and the API waits for that healthy state rather than merely for the container to exist.
- **Named volume `pgdata`** — database files live in a Docker-managed volume so your data survives `docker compose down` and container recreation. Without it, every restart would wipe the database.
- **Environment as configuration.** `ConnectionStrings__Postgres` is the double-underscore convention from *Environment Variables and How .NET Reads Them* above: `IConfiguration` reads it as `ConnectionStrings:Postgres`.

Bring the stack up with `docker compose up --build`, and tear it down (keeping the volume) with `docker compose down`. Add `-v` to also delete volumes.

> **Pitfall:** `depends_on` without a health condition only waits for the container to *start*, not for the service inside to be *ready*. Your app can still race ahead of a not-yet-listening database. Use healthchecks — and build retry logic into your app's startup regardless, because in production (Kubernetes) `depends_on` doesn't exist at all.

## Container Registries

You've built an image locally. To run it on a server or in Kubernetes, you push it to a **registry** — a versioned store for images, like NuGet for containers. Images are named `registry/repository:tag`.

- **Docker Hub** — the default public registry. Great for open-source base images; beware anonymous pull rate limits and prefer official/verified publishers.
- **Azure Container Registry (ACR)** — Microsoft's managed registry, tightly integrated with Azure AD and AKS. Login: `az acr login --name myregistry`, image name `myregistry.azurecr.io/myapi:1.4.0`.
- **Amazon ECR** — the AWS equivalent, integrated with IAM and EKS.
- **GitHub Container Registry (GHCR)** — `ghcr.io/owner/myapi:1.4.0`, convenient when your CI already lives in GitHub Actions.

The push/pull cycle:

```bash
docker build -t myregistry.azurecr.io/myapi:1.4.0 .
docker push myregistry.azurecr.io/myapi:1.4.0
docker pull myregistry.azurecr.io/myapi:1.4.0
```

> **Best practice:** Tag images with an immutable, meaningful version — a semver like `1.4.0` or the git commit SHA. Avoid deploying `latest` to production: it's a moving target, so you can never be certain which build is actually running, and rollbacks become guesswork.

## Viewing Logs

On a server where the app runs as a systemd service ([Chapter 26](#systemd-running-a-net-app-as-a-service)), systemd captures its stdout/stderr into the **journal**:

```bash
journalctl -u myapp                 # all logs for the myapp unit
journalctl -u myapp -f              # follow live (like tail -f)
journalctl -u myapp --since "10 min ago"
journalctl -u myapp -p err          # only error-priority and worse
journalctl -u myapp -n 100          # last 100 lines
```

For containers, the runtime captures stdout/stderr and you read it with:

```bash
docker logs myapp                   # all logs from a container
docker logs -f --tail 100 myapp     # follow the last 100 lines
kubectl logs -f deploy/myapp        # in Kubernetes
```

> **Best practice:** In containers, log to **stdout/stderr**, not to a file. The whole ecosystem — Docker, Kubernetes, cloud log aggregators — expects logs on stdout. .NET's default console logger does exactly this, so leave it that way and let the platform collect and ship the logs.

## Networking Tools

When your app "isn't responding," you need to see what's listening and reach it directly.

```bash
ss -tlnp                 # TCP (t) listening (l) sockets, numeric (n), + PID (p)
ss -tlnp | grep 5000     # is anything listening on port 5000?
netstat -tlnp            # older equivalent, if ss isn't available
```

`ss` (socket statistics) is the modern replacement for `netstat`. Seeing your process bound to the expected port confirms the app started and bound correctly. A common bug: the app binds to `localhost` (127.0.0.1) inside a container, so it's unreachable from outside. Bind to `0.0.0.0` (all interfaces) via `ASPNETCORE_URLS=http://+:8080`.

Then test the endpoint from the same host, ruling out network/firewall issues:

```bash
curl -v http://localhost:8080/health     # -v shows the connection + headers
```

If `curl` from inside the box works but external access fails, the problem is networking (port mapping, firewall, security group), not your app.

## Sources & Further Reading

- **Microsoft Learn: .NET on Linux** — official guidance on installing, running, and containerizing .NET on Linux, including environment-variable configuration and the `ASPNETCORE_`/`DOTNET_` prefixes. https://learn.microsoft.com/dotnet/core/install/linux
- **Microsoft Learn: Host ASP.NET Core on Linux with systemd / Nginx** — the canonical systemd unit-file walkthrough for .NET apps.
- **Microsoft Learn: .NET container images and running as non-root** — guidance on the `app` user and the `USER` instruction.
- **The Linux man pages** — the authoritative reference for every command; access with `man <command>` (e.g. `man chmod`, `man ss`, `man systemctl`).
- **"The Linux Command Line" by William Shotts** — an excellent, free, book-length introduction to the shell, filesystem, permissions, and scripting. https://linuxcommand.org/tlcl.php
- **systemd documentation (`man systemd.service`, `man journalctl`)** — reference for unit-file directives and journal querying.
- **Docker documentation: stop, signals, and PID 1** — explains SIGTERM/SIGKILL behavior and the exec-vs-shell ENTRYPOINT distinction.

## Three questions

**1.** Every deployment of a service takes exactly the grace period to stop its old containers (10 seconds under `docker stop`, 30 in Kubernetes), requests in flight at that moment fail, and the stopped container's exit code is 137. The Dockerfile ends with `ENTRYPOINT dotnet MyApi.dll`. What is happening?

<details>
<summary>Answer</summary>

- **The shell form makes the shell PID 1.** `ENTRYPOINT dotnet MyApi.dll` is stored as `["/bin/sh", "-c", "dotnet MyApi.dll"]`, and on the Debian-based images the shell (dash) starts `dotnet` as its child instead of replacing itself with it ([Processes & Signals](#processes-signals-graceful-shutdown)).
- **SIGTERM goes to PID 1, and this PID 1 ignores it.** The runtime signals only the container's PID 1. The kernel doesn't apply a signal's default action to the PID 1 of a namespace, so a process that installed no handler, as the shell didn't, simply doesn't react, and it doesn't pass the signal on to its child. .NET, which would have started a graceful shutdown, never hears about it.
- **So the grace period always runs out.** After it, the runtime sends SIGKILL, which can't be ignored, and kills everything at once, mid-request. 137 is 128 + 9: terminated by signal 9, SIGKILL. A `docker stop` against a container whose PID 1 was `sh -c` with a child process took 10.2 seconds and exited with 137.

Fix: use the exec form, `ENTRYPOINT ["dotnet", "MyApi.dll"]`, so `dotnet` is PID 1 and the Generic Host receives SIGTERM. If a script must run first, end it with `exec dotnet MyApi.dll`, or run the container with `--init`, which puts a small init process at PID 1 to forward signals. Then check that the application's own shutdown timeout fits inside the orchestrator's grace period.
</details>

**2.** The container is isolated from the host, so why does it matter that its process runs as root?

<details>
<summary>Answer</summary>

- **The isolation is the kernel's, and the kernel is shared.** A container is an ordinary process in namespaces ([The two kernel features that make it work](#the-two-kernel-features-that-make-it-work)). Unless user namespaces are configured, which Docker doesn't do by default, UID 0 inside the container is UID 0 to the host kernel.
- **Every boundary is then one mistake away from the host.** A kernel vulnerability that lets a process escape its namespaces gives the attacker root on the host. So does a configuration slip: a mounted Docker socket, a host-path volume, a container started `--privileged`. Inside the container, root also lets an attacker install tools, rewrite the application's files and read every secret on disk.
- **Bind mounts carry ownership out.** Files a root process writes into a mounted host directory are owned by root on the host.

Fix: run as an unprivileged user, the `app` user (UID 1654) that Microsoft's .NET images ship, or a chiseled image, which defaults to it. Give that user ownership of the few directories it writes to, and add a read-only root filesystem and dropped capabilities ([Running as non-root](#running-as-non-root)).
</details>

**3.** An image's Dockerfile copies the source, builds, and ends with `RUN rm -rf /src /root/.nuget`. The image is still several hundred megabytes larger than the published application. Why didn't the `rm` help?

<details>
<summary>Answer</summary>

- **Layers are immutable, and each one is a diff.** The `COPY` and the build each produced a layer containing those files. The `rm` produces a new layer that records "these paths are deleted" (a whiteout); the earlier layers, and every byte in them, remain part of the image, pulled and stored with it ([Layers and layer caching](#layers-and-layer-caching)).
- **The files are invisible, not gone.** In a running container the deleted paths don't exist, which is what makes the cleanup look as if it worked. `docker history <image>` shows the truth: one layer per instruction with its size, and the large layers below the small `rm`. A test image that wrote a 50 MB file in one `RUN` and deleted it in the next showed a 50 MB layer followed by a 4.1 kB one.
- **The base image counts too.** Building on the SDK image ships the whole SDK, whatever you delete afterwards.

Fix: a multi-stage build ([Multi-stage builds](#multi-stage-builds)). Build and publish in an SDK stage, then copy only the published output into a fresh runtime (or chiseled) stage; nothing from the build stage reaches the final image. Where a single `RUN` creates temporary files, delete them in the same `RUN`.
</details>

## Check at work

**Inspect.** In each Dockerfile your team ships:

- the `ENTRYPOINT` and `CMD`: the exec form (`["dotnet", "App.dll"]`), or a script that ends with `exec`;
- a `USER` instruction, or a chiseled base image; without either the process runs as root;
- a multi-stage build whose final `FROM` is a runtime image, and a `.dockerignore` next to the Dockerfile;
- base images pinned to a version (`aspnet:10.0`, or a digest), not `latest`, and no secrets in `ENV` or `ARG`.

Then compare the orchestrator's grace period (`terminationGracePeriodSeconds`) with the application's `HostOptions.ShutdownTimeout`: the application must finish inside the orchestrator's window.

**Measure.** For the production image: its size (`docker image ls`), its largest layers (`docker history <image>`), and the user it runs as (`docker inspect --format '{{.Config.User}}' <image>`; empty means root). Then start a container, send it a request, run `docker stop` and time it: a stop that takes the whole timeout and an exit code of 137 mean SIGTERM never reached .NET.

## Interview Questions

**What is the difference between a container and a virtual machine?**

<details>
<summary>Answer</summary>

A virtual machine virtualizes hardware and runs a complete guest operating system with its own kernel. A container is a process on the host's kernel, isolated by namespaces and limited by cgroups; there is no second kernel to boot, which is why containers start in milliseconds and cost little at idle. The flip side is a weaker boundary: a kernel bug is shared by every container on the host ([What a Container Actually Is](#what-a-container-actually-is)).
</details>

**What do namespaces and cgroups each do?**

<details>
<summary>Answer</summary>

Namespaces decide what a process can see: its own process tree with itself as PID 1, its own network stack and ports, its own filesystem root and hostname. Cgroups decide what it can consume: CPU, memory and I/O. A Kubernetes memory limit is a cgroup limit, and a process that exceeds it is killed by the kernel's out-of-memory killer ([The two kernel features that make it work](#the-two-kernel-features-that-make-it-work)).
</details>

**An image and a container: what is the difference, and where does a file written inside a container go?**

<details>
<summary>Answer</summary>

An image is a read-only stack of layers plus metadata; a container is a running instance of it with a thin writable layer on top. A write lands in that writable layer through copy-on-write and disappears with the container, so anything that must survive goes to a volume or an external store ([Images vs. Containers](#images-vs-containers)).
</details>

**How do you order a Dockerfile for fast builds, and why does the order matter?**

<details>
<summary>Answer</summary>

Each instruction produces a layer, and the cache reuses a layer only while its instruction and inputs are unchanged; the first change invalidates that layer and every one after it. So copy the project files and run `dotnet restore` first, then copy the rest of the source: editing a `.cs` file then rebuilds only the layers after the restore ([Layers and layer caching](#layers-and-layer-caching)).
</details>

**What does a multi-stage build give you?**

<details>
<summary>Answer</summary>

A build stage based on the SDK image compiles and publishes, and the final stage starts from the much smaller runtime (or chiseled) image and copies in only the published output. The SDK, the source and the NuGet cache never reach the final image, which makes it smaller to pull and gives an attacker less to work with ([Multi-stage builds](#multi-stage-builds)).
</details>

**SIGTERM or SIGKILL: what happens to a .NET app in each case?**

<details>
<summary>Answer</summary>

SIGTERM can be handled: the Generic Host catches it, signals `ApplicationStopping`, stops hosted services and drains requests in flight. SIGKILL can't be caught; the kernel ends the process immediately, mid-request. Orchestrators send SIGTERM, wait the grace period, then send SIGKILL, so the app must actually receive SIGTERM (exec-form `ENTRYPOINT`) and finish within the period ([Processes & Signals](#processes-signals-graceful-shutdown)).
</details>

**A containerized app running as a non-root user throws `UnauthorizedAccessException` writing to `/app/data`. Why, and what is the right fix?**

<details>
<summary>Answer</summary>

The directory was created during the build as root with mode `755`, so only root may create files in it, and the process runs as UID 1654. The fix is to give the runtime user the directory (`RUN mkdir -p /app/data && chown -R app:app /app/data` before `USER app`), or, for a mounted volume, to match its ownership; switching back to root trades a permission error for a security hole ([Permissions](#permissions-why-your-container-app-cant-write-that-file)).
</details>

**The app works with `curl` inside the container but is unreachable from outside. What do you check first?**

<details>
<summary>Answer</summary>

What it listens on. `ss -tlnp` inside the container shows the bound address: an app bound to `127.0.0.1` accepts connections only from inside its own network namespace. Bind to all interfaces (`ASPNETCORE_URLS=http://+:8080`, or `ASPNETCORE_HTTP_PORTS`, which the .NET 8+ images set to 8080), then check the port mapping and the firewall ([Networking Tools](#networking-tools)).
</details>
