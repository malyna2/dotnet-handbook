# Chapter 30: Linux & the Command Line for .NET Developers

_⏱️ Estimated read time: ~33 min ·     3976 words (study pace)_

For most of its life, .NET meant Windows. You wrote C# in Visual Studio, pressed F5, deployed to IIS, and rarely thought about the operating system underneath. That world still exists, but it is no longer where most new .NET code *runs*. Since .NET Core, the runtime is cross-platform, open source, and — crucially — the default target for cloud deployment is a Linux container.

If you want to move from mid-level to senior, being fluent on Linux is not optional. The senior developer is the one who can SSH into a misbehaving box at 2 a.m., read the journal, spot that the process was killed by the OOM killer, notice the container is running as UID 1654 and can't write to a volume, and fix it — without opening a GUI. This chapter gets you there.

## Why Linux Matters for Modern .NET

Three forces pushed .NET onto Linux, and understanding them tells you *why* the skills in this chapter pay off.

**Containers.** Docker images are almost always built `FROM` a Linux base. Microsoft ships `mcr.microsoft.com/dotnet/aspnet:8.0` as a Debian- or Alpine-based Linux image by default. When you `docker build` and `docker run`, you are running your app on a tiny Linux system, even if your laptop is Windows. Kubernetes — the industry standard orchestrator — schedules Linux containers.

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

## Permissions: Why Your Container App Can't Write That File

This is the single most common Linux surprise for Windows developers, so we'll go deep.

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

Now the classic container failure. Your Dockerfile switches to a non-root user for security:

```dockerfile
USER app        # runs as UID 1654, not root
```

Your app then tries to write to `/app/data`, but that directory was created earlier in the build as `root` with `755` — meaning only root can write. At runtime your process is UID 1654, gets **write denied**, and .NET throws `UnauthorizedAccessException`. The fix is to give ownership to the runtime user during the build:

```dockerfile
RUN mkdir -p /app/data && chown -R app:app /app/data
USER app
```

> **Pitfall:** Mounted volumes bring their *host* ownership into the container. A volume owned by host UID 0 mounted into a container running as UID 1654 will be unwritable no matter what your Dockerfile does. Match the UIDs, or set ownership on the volume, or run an init step as root that `chown`s the mount.

> **Best practice:** Run containers as non-root. The official .NET 8+ images include a pre-created `app` user (UID 1654) and even default to it. Don't undo that for convenience — a compromised root container is a compromised host.

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

This ties directly into containers. When Kubernetes or `docker stop` shuts down your app, it sends **SIGTERM**, waits a grace period (default 30s in Docker, `terminationGracePeriodSeconds` in K8s), and only then sends **SIGKILL**. ASP.NET Core's generic host listens for SIGTERM and triggers `IHostApplicationLifetime.ApplicationStopping`, runs your `IHostedService.StopAsync`, and drains requests. If your app ignores SIGTERM or takes too long, it gets SIGKILL'd mid-request.

> **Best practice:** Make sure .NET actually *receives* SIGTERM. Use the **exec form** of `ENTRYPOINT` (`ENTRYPOINT ["dotnet", "MyApp.dll"]`), not the shell form (`ENTRYPOINT dotnet MyApp.dll`). The shell form runs your app as a child of `/bin/sh`, which is PID 1 and does not forward signals — so SIGTERM never reaches .NET and every shutdown is a hard kill.

### Foreground, Background, and Jobs

A command normally runs in the **foreground**, tying up your terminal. Append `&` to run it in the **background**:

```bash
dotnet MyApp.dll &      # runs in background, prints a job number and PID
jobs                    # list background jobs in this shell
fg %1                   # bring job 1 back to the foreground
# Ctrl+Z suspends the foreground job; bg %1 resumes it in the background
```

For anything long-lived on a server you'd use systemd (below) or a container, not `&` — background jobs die when your shell exits.

## Essential Commands

These are the verbs of daily Linux life. Learn them until they're muscle memory.

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

## Environment Variables and How .NET Reads Them

Environment variables are key–value pairs the shell passes to programs. They're the primary way you configure a containerized app — no config file rebuild needed.

```bash
export ASPNETCORE_ENVIRONMENT=Production   # set for this shell and children
echo $ASPNETCORE_ENVIRONMENT               # read it back
printenv                                   # list all environment variables
NAME=value dotnet MyApp.dll                # set just for this one command
```

.NET's configuration system reads environment variables automatically. Two conventions matter:

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

> **Best practice:** Never commit secrets to `appsettings.json`. Use environment variables (or a secret store) for anything sensitive. The `__` convention lets env vars cleanly override file-based config, which is exactly the precedence order .NET applies.

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

## systemd: Running a .NET App as a Service

When you deploy to a plain Linux VM instead of a container, you need your app to start on boot, restart on crash, and log properly. **systemd** is the init system that manages this. You describe your service in a **unit file** under `/etc/systemd/system/`.

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

Then manage it:

```bash
sudo systemctl daemon-reload        # tell systemd to re-read unit files
sudo systemctl enable myapp         # start automatically on boot
sudo systemctl start myapp          # start it now
sudo systemctl status myapp         # is it running? recent log lines
sudo systemctl restart myapp        # restart after a deploy
```

`Restart=always` gives you crash recovery; `User=www-data` runs the app unprivileged. Note that systemd sends **SIGTERM** on `stop`, so the same graceful-shutdown handling from containers applies here.

**Container vs. service:** in a container you don't use systemd — the container runtime *is* your process supervisor, and your app is PID 1. Use systemd for traditional VM deployments; use the container orchestrator's restart policy for containerized ones. Don't try to run systemd inside a container.

## Viewing Logs

systemd captures your app's stdout/stderr into the **journal**:

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

## Shell Scripting for Automation

A shell script bundles commands into a reusable file. You'll write these for deploys, health checks, and CI glue. Here's an annotated deploy-and-verify script:

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

Key ideas: the **shebang** picks the interpreter; **`set -euo pipefail`** is the single most important line for robust scripts (fail fast, fail loud); `$VAR` reads variables; the `for` loop with `curl -sf` (silent, fail-on-error) polls the health endpoint; a non-zero `exit` tells CI the deploy failed.

> **Best practice:** Start every non-trivial script with `set -euo pipefail`. Without it, a failed command in the middle is silently ignored and the script marches on, often making things worse. This one line turns sloppy scripts into safe ones.

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

## Live-Container Triage: A Walkthrough

Here is how the pieces of this chapter combine on a real page: "the orders container keeps dying, and now it's up but slow."

Start with the logs. `docker logs --tail 200 orders` shows normal request logs that simply *stop* mid-flight — no exception, none of your graceful-shutdown lines. That silence is a signature: the app didn't crash, it was SIGKILLed — a SIGTERM would have produced those shutdown lines. Confirm on the host: `journalctl --since "1 hour ago" | grep -i oom` turns up the kernel's OOM killer reaping your dotnet process. The container hit its memory limit; the kernel ended the argument (exit code 137 = 128 + 9, i.e. SIGKILL).

The replacement container is up but sluggish, so check the sockets next. `ss -tlnp | grep 8080` shows the process correctly bound to `0.0.0.0:8080` — it started and it's reachable. But drop the `l` and look at established connections: `ss -tnp | grep 8080` scrolls for pages. Connections are piling up, which means requests are arriving faster than they complete — the app is alive but drowning, not dead.

Now the resource view. `top` shows the dotnet process at modest CPU but with resident memory already climbing toward the limit again; `ps aux | grep dotnet | sort -k4 -rn | head` confirms it. So it's a cycle: memory grows, the OOM killer fires, the container restarts, and the connection backlog makes everything slow in between.

This is as far as the OS view goes. It has told you *that* memory grows and *when* the kernel kills you — but not *what* is growing. For that you attach the runtime-level tools from Chapter 15: `dotnet-counters` to watch GC heap size, allocation rate, and thread-pool queue length live, and `dotnet-dump`/`dotnet-gcdump` to see which types are accumulating and what roots them. The comparison is the diagnosis: if the GC heap is flat while the working set climbs, suspect native or buffer memory — or a limit set below the app's honest working set; if Gen 2 and the LOH climb together, you have a managed leak.

That is the method: the OS view (`journalctl`, `ss`, `top`) shows what the machine sees, the runtime view (Chapter 15's `dotnet-*` tools) shows what the CLR sees — and it's the *disagreement* between them that tells you which layer is lying.

## Putting It Together

The through-line of this chapter is that Linux is now the *native habitat* of production .NET. The filesystem tree, permission bits, signals, and pipes aren't trivia — they're the exact concepts that explain why a container won't start, why an app can't write a file, why a deployment hangs on shutdown, or why the port isn't reachable. When you can reason about these from the shell, you stop being dependent on someone else to diagnose production, and that self-sufficiency is exactly what separates a senior engineer from a mid-level one.

Practice by doing: spin up an Ubuntu container (`docker run -it ubuntu bash`), poke around `/etc` and `/var`, break a permission and fix it, run your app as a non-root user, and read its logs with `journalctl` or `docker logs`. The commands become reflexes faster than you'd expect.

## Sources & Further Reading

- **Microsoft Learn: .NET on Linux** — official guidance on installing, running, and containerizing .NET on Linux, including environment-variable configuration and the `ASPNETCORE_`/`DOTNET_` prefixes. https://learn.microsoft.com/dotnet/core/install/linux
- **Microsoft Learn: Host ASP.NET Core on Linux with systemd / Nginx** — the canonical systemd unit-file walkthrough for .NET apps.
- **Microsoft Learn: .NET container images and running as non-root** — guidance on the `app` user and the `USER` instruction.
- **The Linux man pages** — the authoritative reference for every command; access with `man <command>` (e.g. `man chmod`, `man ss`, `man systemctl`).
- **"The Linux Command Line" by William Shotts** — an excellent, free, book-length introduction to the shell, filesystem, permissions, and scripting. https://linuxcommand.org/tlcl.php
- **systemd documentation (`man systemd.service`, `man journalctl`)** — reference for unit-file directives and journal querying.
- **Docker documentation: stop, signals, and PID 1** — explains SIGTERM/SIGKILL behavior and the exec-vs-shell ENTRYPOINT distinction.
