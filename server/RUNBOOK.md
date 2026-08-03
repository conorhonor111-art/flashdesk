# FlashDesk relay server — rebuild runbook

**Why this file exists.** The relay holds no data, only configuration. So instead of paying the
provider for backups, everything needed to recreate the server from a blank Ubuntu image is
written here. Losing the server costs ~20 minutes of rebuilding, not money. Keep this file
updated **as changes are made**, not afterwards — a step that only lives in someone's head is a
step that will be missing on rebuild day.

Deployment order matters and is preserved below. Every command runs **on the server** unless it
says `.223`.

---

## 0. Facts about the current server

| | |
|---|---|
| Provider | DeltaHost (Kyiv, Ukraine) |
| IP | `139.28.36.247` |
| Plan | 4 GB RAM · 50 GB NVMe · 10 TB traffic · $15/month |
| OS | Ubuntu 24.04 LTS (Cloudinit image) |
| Measured latency from `.223` | **0.1 ms** (5 hops, all domestic) |
| Access | SSH key only, port 22 |
| Private key | `C:\Users\PC\.ssh\flashdesk_relay` on `.223` — never leaves that machine |
| Public key | `ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIIy+33Xq8m92HD0jgZwXLQi4mTDwf4hIqk+VL2ztfPA0 flashdesk-relay` |
| Fallback access | Provider panel → Manage → VPS → VNC/KVM console, `root` + the password Conor set |

**Trap, learned the hard way:** DeltaHost's *generated* root password was rejected by both the
console and SSH until support reset it manually. Do not rely on a provider-generated password;
get the key in early and treat the console as the emergency route only.

---

## 1. Get the key in (from the provider's VNC console, before SSH works)

Log in at the console as `root`, then paste one line:

```bash
mkdir -p /root/.ssh && echo 'ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIIy+33Xq8m92HD0jgZwXLQi4mTDwf4hIqk+VL2ztfPA0 flashdesk-relay' > /root/.ssh/authorized_keys && chmod 700 /root/.ssh && chmod 600 /root/.ssh/authorized_keys && echo KEY-INSTALLED-OK
```

Then set a console password that is known only to Conor: `passwd`

From `.223` everything after this runs over SSH:

```bash
ssh -i /c/Users/PC/.ssh/flashdesk_relay root@139.28.36.247
```

---

## 2. Key-only SSH

**The gotcha:** files in `/etc/ssh/sshd_config.d/` are read in **name order** and the **first**
value wins. Ubuntu's cloud image ships `60-cloudimg-settings.conf` with
`PasswordAuthentication yes`, so a `99-` file is silently ignored. Our policy must sort earlier.

```bash
cat > /etc/ssh/sshd_config.d/10-flashdesk.conf <<'CONF'
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin prohibit-password
PubkeyAuthentication yes
CONF
sed -i 's/^PasswordAuthentication yes/#PasswordAuthentication yes  # disabled by FlashDesk setup/' /etc/ssh/sshd_config.d/60-cloudimg-settings.conf
sshd -t && systemctl restart ssh          # ALWAYS validate first, or you lock everyone out
sshd -T | grep -E '^(passwordauthentication|permitrootlogin|pubkeyauthentication)'
```

Verify from `.223` — do not trust the config file, test it:

```bash
ssh -o PreferredAuthentications=password -o PubkeyAuthentication=no root@139.28.36.247   # must be refused
ssh -i /c/Users/PC/.ssh/flashdesk_relay root@139.28.36.247 'whoami'                      # must print root
```

Note: this affects SSH only. Root can still log in at the VNC console — that is the deliberate
fallback.

---

## 3. Updates and automatic security patches

```bash
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get -y upgrade -o Dpkg::Options::=--force-confold -o Dpkg::Options::=--force-confdef
apt-get -y install unattended-upgrades
systemctl enable --now unattended-upgrades
[ -f /var/run/reboot-required ] && echo "reboot needed" || echo "no reboot needed"
```

---

## 4. Firewall

**Allow 22 before enabling**, or the enable command locks you out of your own session.

```bash
apt-get -y install ufw
ufw allow 22/tcp   comment 'SSH admin'
ufw allow 80/tcp   comment 'HTTP - Let us Encrypt checks + redirect'
ufw allow 443/tcp  comment 'HTTPS - relay + health'
ufw default deny incoming
ufw default allow outgoing
ufw --force enable
ufw status verbose
```

Verify from `.223` (from outside, not by reading `ufw status`):

```powershell
foreach ($p in 22,80,443,3306) { Test-NetConnection 139.28.36.247 -Port $p -InformationLevel Quiet }
# expect: 22 True, 3306 False
```

---

## 5. Caddy (web server + automatic TLS)

```bash
apt-get -y install debian-keyring debian-archive-keyring apt-transport-https curl
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' > /etc/apt/sources.list.d/caddy-stable.list
apt-get update -qq && apt-get -y install caddy
```

Placeholder page (proves the web layer works before the relay exists) —
`/var/www/relay/index.html`, owned by `caddy:caddy`; see `site/relay-placeholder.html` in this
repo for the exact content.

`/etc/caddy/Caddyfile`:

```
relay.flashdesk.org {
	encode gzip
	root * /var/www/relay
	file_server
}
```

**Do not add a `log { output file ... }` block.** The packaged systemd unit sandboxes Caddy out
of `/var/log`, and the service will refuse to start with `permission denied`. Caddy's logs go to
the journal instead: `journalctl -u caddy -f`.

```bash
caddy validate --config /etc/caddy/Caddyfile
systemctl restart caddy && systemctl is-active caddy
```

TLS is automatic, but only succeeds **after** DNS resolves (see §6). Until then the journal shows
`NXDOMAIN looking up A for relay.flashdesk.org` and Caddy retries by itself — that is expected,
not a fault.

---

## 6. DNS (done in cPanel, NOT at the domain registrar)

`flashdesk.org` uses the hosting's nameservers (`ns1/ns2.hostsilo.com`), so records are edited in
**cPanel → Domains → Zone Editor**, not in the Namecheap/registrar panel.

| Name | Type | Value | Purpose |
|---|---|---|---|
| `flashdesk.org` | A | `64.187.97.203` | the cPanel shared hosting — site + `/download` |
| `relay` | A | `139.28.36.247` | this VPS — relay only |

Check propagation: dnschecker.org → `relay.flashdesk.org` → type A. Typically 15–60 minutes.

---

## 7. The relay service

Runtime from Ubuntu's own repository (so `unattended-upgrades` patches it; no third-party repo):

```bash
apt-get -y install aspnetcore-runtime-8.0
dotnet --list-runtimes
```

Build on `.223` and copy up:

```bash
dotnet publish src\RemoteDesktop.Relay -c Release -r linux-x64 --self-contained false -o <out>
scp -i /c/Users/PC/.ssh/flashdesk_relay <out>/* root@139.28.36.247:/opt/flashdesk-relay/
```

On the server: dedicated unprivileged user, state directory, systemd unit.

```bash
useradd --system --no-create-home --shell /usr/sbin/nologin flashdesk
mkdir -p /var/lib/flashdesk-relay
chown -R flashdesk:flashdesk /opt/flashdesk-relay /var/lib/flashdesk-relay
```

`/etc/systemd/system/flashdesk-relay.service` — the copy in the repo is authoritative; key
points: `User=flashdesk` (never root), `Restart=always` + `RestartSec=5` (crash recovery),
`WantedBy=multi-user.target` (reboot recovery), and the sandbox block
(`ProtectSystem=strict`, `ProtectHome`, `NoNewPrivileges`, `RestrictAddressFamilies`,
`ReadWritePaths=/var/lib/flashdesk-relay`) so a compromised relay cannot touch the machine.

```bash
systemctl daemon-reload && systemctl enable --now flashdesk-relay
curl -sS http://127.0.0.1:5000/health
```

Caddyfile gains a `handle /health` block that reverse-proxies to `127.0.0.1:5000`; everything
else keeps serving the static placeholder.

**Verify (from `.223`, over the internet — not by reading service status):**

```bash
curl -sS https://relay.flashdesk.org/health          # must print status: OK
ssh ... 'kill -9 $(systemctl show -p MainPID --value flashdesk-relay)'
sleep 8 && curl -sS https://relay.flashdesk.org/health   # must answer again, NRestarts incremented
```

## 7a. Reboot resilience — tested 2026-08-03, repeat after any change to boot-time services

**Measured downtime: 16 seconds** (last reply → first reply again; server back with everything
running). That is what a reboot costs a client mid-session: a ~16 s interruption, which is why
Stage 3's client reconnect logic uses a neutral "Connection lost — reconnecting…" state rather
than an error, and why the heartbeat expiry is 60 s (see CLAUDE.md).

**Check SSH will survive BEFORE rebooting.** Ubuntu 24.04 uses socket activation:
`systemctl is-enabled ssh.service` reports **disabled**, which looks alarming and is fine —
`ssh.socket` is the enabled unit. Verify with:

```bash
systemctl is-enabled ssh.socket    # must be: enabled
```

All four of these must be re-verified after a reboot; the last two are the ones a silent reset
would hurt most and nobody thinks to check:

| What | How to prove it (from `.223`, not by reading status on the server) |
|---|---|
| Relay | `curl -sS https://relay.flashdesk.org/health` → `status : OK` |
| TLS | `curl -o /dev/null -w '%{ssl_verify_result}'` → `0` |
| Firewall | open a TCP connection to 22 and 443 (must work) and 3306 (must fail) |
| Password login off | `ssh -o PreferredAuthentications=password -o PubkeyAuthentication=no root@…` → `Permission denied (publickey)` |

## 8. Still to come (update this file as each lands)

- WebSocket endpoint + FlashDesk ID registry, rate limiting, heartbeat expiry
  (see CLAUDE.md "The FlashDesk ID system")
- Adaptive quality / frame rate driven by measured bandwidth
- `/download` served from the cPanel side (not this server)
