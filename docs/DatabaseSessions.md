# Database sessions (PostgreSQL)

SuperXPuTTY can show sessions from a PostgreSQL table, under **Database sessions** in the Sessions tree, next to Saved and Private sessions. This guide is for whoever manages that table: by hand, with SQL, or from a script such as an inventory sync. How the app side works is in [FORK_CHANGES.md](../FORK_CHANGES.md) ("Database sessions").

## How SuperXPuTTY uses the table

- **Setup:** Tools → **Database sessions...** sets the connection (host, port, database, schema, table, user, password, SSL). **Create / upgrade schema** creates the database, schema and table, or adds whatever is missing. It never runs on its own, and never when **Read only** is ticked. Run it again after updating SuperXPuTTY, to add new columns.
- **Reading:** the whole table, in pages of 500, at start-up, on **Refresh** (right-click Database sessions), when the settings change, after each change made in SuperXPuTTY, and every 2 minutes while the database can't be reached. Changes your script makes show up at the next of those; there's no live update yet.
- **Offline:** the last read is cached beside each user's settings, so the sessions still open when the database is down. Changes made in SuperXPuTTY while it's down are queued and sent when it's back.
- **Writing:** a user can add, edit, move and delete sessions in the tree if their login has INSERT, UPDATE and DELETE on the table and Read only isn't ticked. Each save of an edit checks `updated_at`, so one person's change isn't silently overwritten by another's (or by your script's).
- **The connection's application name is `SuperPuTTY`.** The guard trigger uses it to tell SuperXPuTTY from your scripts (see [Locks](#locks)). Give your scripts their own application name.

## The table

Default name: `superputty.sessions` (database `superputty`). All three names can be changed in the dialog.

| Column | Type | Default | Meaning |
|---|---|---|---|
| `id` | uuid | `gen_random_uuid()` | Row key. Leave it to the default |
| `folder` | text | `''` | Path in the tree, `/`-separated, e.g. `HQ/Core`; `''` puts the session at the top. No leading or trailing `/` |
| `name` | text | (required) | Session name, shown in the tree. Not empty, no `/`. Unique within its folder |
| `protocol` | text | `SSH` | One of `SSH`, `Telnet`, `Rlogin`, `Raw`, `Serial`, `VNC`, `RDP`, `Mintty`, `Cygterm`, `WSL`, `WINCMD` (cmd), `PS` (PowerShell). Case doesn't matter; anything else is read as SSH |
| `host` | text | `''` | Host name or address. Placeholders like `{site}` work |
| `port` | integer | `0` | `0` means the protocol's usual port: 22 for SSH, 23 Telnet, 513 Rlogin, and the client's own default for RDP and VNC. Set it for Raw and anything non-standard |
| `username` | text | `''` | Login name. Use a placeholder such as `{user}` so each user logs in as themselves |
| `putty_profile` | text | `Default Settings` | The PuTTY (or KiTTY) saved session to start from |
| `extra_args` | text | `''` | Extra PuTTY arguments. Don't put passwords here (`-pw`): the table is shared |
| `icon` | text | NULL | The tree icon (see [Icons](#icons)) |
| `notes` | text | NULL | Shown in the session's tooltip |
| `spsl_script` | text | NULL | Path or URL of an SPSL script run when the session opens |
| `rdp_options` | text | NULL | For RDP: overrides of Tools → Options → RDP, as `Key=Value;Key=Value` (see [RDP options](#rdp-options)) |
| `local_path`, `remote_path` | text | NULL | Starting folders for the SCP file browser |
| `locked` | boolean | `false` | Lock the whole session against changes from SuperXPuTTY |
| `locked_fields` | text[] | `'{}'` | Lock only these columns, e.g. `'{host,port}'` |
| `managed_by` | text | NULL | Which system owns the row, e.g. `'inventory'`. NULL for sessions people made in SuperXPuTTY |
| `external_id` | text | NULL | The owning system's own key for it, e.g. the inventory's device ID. Unique per `managed_by` |
| `created_at` | timestamptz | `now()` | When the row was added |
| `updated_at` | timestamptz | `now()` | Last change; kept current by a trigger |
| `updated_by` | text | `current_user` | Database login that made the last change; kept current by a trigger |

**Passwords never go in the table.** Use `{key}` placeholders. Each user sets the values in Tools → Environment Variables (secret values are encrypted for their Windows login), and SuperXPuTTY fills them in when the session opens.

### What the database does for you

Create / upgrade schema adds these; they're named after the table.

| Object | What it does |
|---|---|
| `UNIQUE (folder, name)` | No two sessions with the same name in one folder |
| `CHECK`s | `name` not empty and without `/`; `folder` without a leading or trailing `/` |
| Index `<table>_external_id` | Unique `(managed_by, external_id)` where `external_id` is set: the key for upserts |
| Trigger `<table>_touch` | Sets `updated_at` and `updated_by` on every insert and update |
| Trigger `<table>_guard` | Enforces locks, but only for connections named `SuperPuTTY` |
| Trigger `<table>_changed` | `NOTIFY superputty_sessions` after every change (for live updates later) |

## Locks

Locks are for sessions something else owns. They stop SuperXPuTTY changing what your script will set again anyway.

| Set | SuperXPuTTY then |
|---|---|
| `locked = true` | Opens and connects it, but won't edit, delete, rename or move it, or rename or remove a folder holding it. This is "lock everything" |
| `locked_fields = '{host,port}'` | Shows those fields greyed and read-only in Edit Session. The other fields (notes, icon, ...) can still be changed, and the session can still be deleted. Include `folder` to stop it being moved, and `name` to stop it being renamed |

- **Field names:** `locked_fields` takes column names: `folder`, `name`, `protocol`, `host`, `port`, `username`, `putty_profile`, `extra_args`, `icon`, `notes`, `spsl_script`, `rdp_options`, `local_path`, `remote_path`.
- **Columns SuperXPuTTY never changes:** `locked`, `locked_fields`, `managed_by`, `external_id` and `created_at`. The guard trigger refuses that too.
- **Enforcement:** the guard trigger refuses SuperXPuTTY's changes to locked rows and fields, so even an older copy of the app can't get round them. Users see e.g. *Host of "core-sw1" is locked in the database*.
- **Your scripts aren't affected,** because they connect under another application name. If a user's own login also runs scripts, those scripts aren't affected either. The lock is about the program, not the login: use [roles](#roles-and-permissions) for that.

## Icons

`icon` takes the same keys as the icon picker in Edit Session:

- **A device or general icon:** `switch`, `l3switch`, `router`, `voicerouter`, `nexus`, `firewall`, `wireless`, `loadbalancer`, `server`, `workstation`, `terminal`, `windows`, `database`, `storage`, `cloud`, `phone`, `camera`, `printer`, `folder`, `flag`, `house`, `globe`, `map`, `truck`, `book`, `document`, `archive`, `email`, `chat`, `rss`, `clock`, `help`, `info`, `lightbulb`, `bolt`, `tv`, `mobile`, `star`, `lock`.
- **A logo:** `almalinux`, `alpinelinux`, `android`, `apple`, `archlinux`, `centos`, `cisco`, `debian`, `docker`, `fedora`, `fortinet`, `freebsd`, `kalilinux`, `kubernetes`, `linux`, `linuxmint`, `openbsd`, `opensuse`, `paloaltonetworks`, `proxmox`, `raspberrypi`, `redhat`, `rockylinux`, `suse`, `ubuntu`, `vmware`.
- **With a colour:** any of those plus `#RRGGBB`, e.g. `switch#1E63B4` for a blue switch.
- **A user's custom icon:** `custom:name`, e.g. `custom:juniper#C62828`. Custom icons are files in each user's settings folder (`<SettingsFolder>\icons`), so they only show for users who have them.
- **Unknown or empty** shows the default computer icon.

## RDP options

`rdp_options` holds only what differs from the user's Tools → Options → RDP, as `Key=Value` pairs separated by `;`, e.g. `DisplayMode=Fixed;FixedWidth=1920;FixedHeight=1080;RedirectDrives=true`.

| Key | Values |
|---|---|
| `DisplayMode` | `FitTab` or `Fixed` |
| `FixedWidth`, `FixedHeight` | Desktop size for `Fixed` |
| `ColorDepth` | `15`, `16`, `24` or `32` |
| `SmartSizing`, `RedirectClipboard`, `RedirectDrives`, `RedirectPrinters`, `RedirectSmartCards`, `RedirectMicrophone`, `ConnectToConsole` | `true` or `false` |
| `AudioMode` | `0` play here, `1` play on the remote, `2` don't play |
| `KeyboardHookMode` | Windows key combinations: `0` here, `1` on the remote, `2` only in full screen |
| `AuthenticationLevel` | When the server can't be authenticated: `0` connect, `1` don't connect, `2` warn |
| `GatewayHost` | RD Gateway host; empty for none |

## Managing sessions from a script

For an inventory system that creates and updates sessions:

1. **Own your rows.** Give every row you create the same `managed_by` (e.g. `'inventory'`) and the inventory's own ID as `external_id`. Rows people make in SuperXPuTTY have both NULL, so your script never mistakes them for its own.
2. **Upsert on `(managed_by, external_id)`,** not on folder and name. A device renamed or moved in the inventory then updates its existing session (keeping its id, `created_at` and anyone's notes) instead of creating a second one. Favourites follow folder and name, so a user's favourite of a session the script renames has to be added again.
3. **Only update the columns you own.** Leave `notes` and `icon` out of the update, so people can annotate and recolour sessions.
4. **Lock what you own** with `locked_fields`, so a user's edit isn't silently undone at the next sync.
5. **Skip updates that change nothing.** `updated_at` then means something, and SuperXPuTTY users editing a session aren't told someone else changed it when nothing did.
6. **Delete your stale rows:** those with your `managed_by` whose `external_id` is no longer in the inventory. Guard against an empty or failed inventory read first; otherwise the delete removes every session you own.
7. **Use your own application name,** e.g. `application_name=inventory-sync`, so the lock trigger doesn't apply to you.
8. **Handle name clashes.** If someone made a session by hand with the same folder and name, the insert fails with a unique violation on `(folder, name)`. Skip it and report it; don't delete their row.

### The SQL

The upsert. `xmax = 0` is true for a row just inserted, so the script can count adds and updates:

```sql
INSERT INTO superputty.sessions AS s
       (managed_by, external_id, folder, name, protocol, host, port, username, icon, locked_fields)
VALUES (%(managed_by)s, %(external_id)s, %(folder)s, %(name)s, %(protocol)s, %(host)s, %(port)s, %(username)s,
        %(icon)s, %(locked_fields)s)
ON CONFLICT (managed_by, external_id) WHERE external_id IS NOT NULL
DO UPDATE SET folder = EXCLUDED.folder, name = EXCLUDED.name, protocol = EXCLUDED.protocol, host = EXCLUDED.host,
              port = EXCLUDED.port, username = EXCLUDED.username, locked_fields = EXCLUDED.locked_fields
        WHERE (s.folder, s.name, s.protocol, s.host, s.port, s.username, s.locked_fields)
              IS DISTINCT FROM (EXCLUDED.folder, EXCLUDED.name, EXCLUDED.protocol, EXCLUDED.host, EXCLUDED.port,
                                EXCLUDED.username, EXCLUDED.locked_fields)
RETURNING (xmax = 0) AS inserted;
```

It returns no row when nothing changed. `icon` is set when the session is added and left alone afterwards.

Removing what's gone from the inventory:

```sql
DELETE FROM superputty.sessions
 WHERE managed_by = %(managed_by)s AND NOT (external_id = ANY(%(ids)s));
```

### Example: a Python sync script

Needs [psycopg](https://www.psycopg.org/psycopg3/) 3 (`pip install "psycopg[binary]"`). Replace `inventory_devices()` with a call to your inventory (an API, a CMDB query, a CSV export).

```python
"""Sync devices from an inventory into SuperXPuTTY's sessions table.

Adds new devices, updates changed ones, removes those no longer in the inventory, and locks the fields the
inventory owns. Sessions people made in SuperXPuTTY (managed_by NULL) are never touched.
"""
import os
import psycopg

DSN = (
    "host=192.168.3.2 dbname=superputty user=inventory_sync "
    f"password={os.environ['SUPERPUTTY_DB_PASSWORD']} application_name=inventory-sync"
)
MANAGED_BY = "inventory"
OWNED_FIELDS = ["folder", "name", "protocol", "host", "port", "username"]  # locked for SuperXPuTTY users
ICONS = {"switch": "switch", "router": "router", "firewall": "firewall", "server": "server"}

UPSERT = """
INSERT INTO superputty.sessions AS s
       (managed_by, external_id, folder, name, protocol, host, port, username, icon, locked_fields)
VALUES (%(managed_by)s, %(external_id)s, %(folder)s, %(name)s, %(protocol)s, %(host)s, %(port)s, %(username)s,
        %(icon)s, %(locked_fields)s)
ON CONFLICT (managed_by, external_id) WHERE external_id IS NOT NULL
DO UPDATE SET folder = EXCLUDED.folder, name = EXCLUDED.name, protocol = EXCLUDED.protocol, host = EXCLUDED.host,
              port = EXCLUDED.port, username = EXCLUDED.username, locked_fields = EXCLUDED.locked_fields
        WHERE (s.folder, s.name, s.protocol, s.host, s.port, s.username, s.locked_fields)
              IS DISTINCT FROM (EXCLUDED.folder, EXCLUDED.name, EXCLUDED.protocol, EXCLUDED.host, EXCLUDED.port,
                                EXCLUDED.username, EXCLUDED.locked_fields)
RETURNING (xmax = 0) AS inserted
"""
DELETE_STALE = """
DELETE FROM superputty.sessions WHERE managed_by = %(managed_by)s AND NOT (external_id = ANY(%(ids)s))
"""


def inventory_devices():
    """The devices to have sessions for. Replace with your inventory's API or database."""
    return [
        {"id": "dev-1001", "site": "HQ", "role": "Core", "hostname": "core-sw1", "ip": "10.0.0.1", "type": "switch"},
        {"id": "dev-1002", "site": "HQ", "role": "Core", "hostname": "core-sw2", "ip": "10.0.0.2", "type": "switch"},
        {"id": "dev-2001", "site": "Branch1", "role": "Edge", "hostname": "br1-rtr1", "ip": "10.1.0.1", "type": "router"},
    ]


def to_row(device):
    """One device as a sessions row."""
    return {
        "managed_by": MANAGED_BY,
        "external_id": device["id"],
        "folder": f"{device['site']}/{device['role']}",
        "name": device["hostname"],
        "protocol": "SSH",
        "host": device["ip"],
        "port": 22,
        "username": "{user}",  # each user's own login, from Tools > Environment Variables
        "icon": ICONS.get(device["type"]),
        "locked_fields": OWNED_FIELDS,
    }


def sync():
    rows = [to_row(d) for d in inventory_devices()]
    if not rows:
        # an empty answer is more likely a failed inventory read than an empty network: deleting would remove everything
        raise SystemExit("the inventory returned no devices; nothing changed")
    added = updated = unchanged = 0
    clashes = []
    with psycopg.connect(DSN) as conn:
        for row in rows:
            try:
                with conn.transaction():  # a savepoint: one bad row doesn't undo the others
                    result = conn.execute(UPSERT, row).fetchone()
            except psycopg.errors.UniqueViolation:
                # someone made a session with this folder and name by hand; leave theirs alone
                clashes.append(f"{row['folder']}/{row['name']}")
                continue
            if result is None:
                unchanged += 1
            elif result[0]:
                added += 1
            else:
                updated += 1
        removed = conn.execute(DELETE_STALE, {"managed_by": MANAGED_BY, "ids": [r["external_id"] for r in rows]}).rowcount
    print(f"added {added}, updated {updated}, unchanged {unchanged}, removed {removed}")
    for clash in clashes:
        print(f"skipped {clash}: a session with that name is already in that folder")


if __name__ == "__main__":
    sync()
```

Run it from a scheduled task or cron job. SuperXPuTTY users see the changes at their next Refresh or start-up.

### Other useful SQL

```sql
-- what the inventory owns, and what people added by hand
SELECT coalesce(managed_by, '(by hand)') AS owner, count(*) FROM superputty.sessions GROUP BY 1;

-- who changed what recently
SELECT folder, name, updated_by, updated_at FROM superputty.sessions ORDER BY updated_at DESC LIMIT 20;

-- lock a whole folder against changes from SuperXPuTTY, and unlock it
UPDATE superputty.sessions SET locked = true  WHERE folder = 'HQ/Core' OR folder LIKE 'HQ/Core/%';
UPDATE superputty.sessions SET locked = false WHERE folder = 'HQ/Core' OR folder LIKE 'HQ/Core/%';

-- hand a session made by hand over to the inventory
UPDATE superputty.sessions SET managed_by = 'inventory', external_id = 'dev-1003' WHERE folder = 'HQ/Core' AND name = 'core-sw3';
```

## Roles and permissions

SuperXPuTTY decides what a user can do from their login's privileges on the table. With only SELECT, Database sessions shows as read-only and its editing commands are disabled. A suggested setup:

```sql
CREATE ROLE superputty_read;    -- may open sessions
CREATE ROLE superputty_edit;    -- may also add, change and remove them in SuperXPuTTY
GRANT CONNECT ON DATABASE superputty TO superputty_read, superputty_edit;
GRANT USAGE ON SCHEMA superputty TO superputty_read, superputty_edit;
GRANT SELECT ON superputty.sessions TO superputty_read;
GRANT SELECT, INSERT, UPDATE, DELETE ON superputty.sessions TO superputty_edit;

-- the sync script: its own login, under its own application name
CREATE ROLE inventory_sync LOGIN PASSWORD 'change-me';
GRANT CONNECT ON DATABASE superputty TO inventory_sync;
GRANT USAGE ON SCHEMA superputty TO inventory_sync;
GRANT SELECT, INSERT, UPDATE, DELETE ON superputty.sessions TO inventory_sync;

-- people
CREATE ROLE alice LOGIN PASSWORD 'change-me' IN ROLE superputty_edit;
```

- **Creating the table** (Create / upgrade schema) needs a login allowed to create the database, schema and triggers, e.g. the owner or a superuser. Do it once, then give users the roles above.
- **Ticking Read only** in SuperXPuTTY makes a user read-only whatever their login may do, and stops it creating anything.

## Upgrading

New versions of SuperXPuTTY may add columns. Until **Create / upgrade schema** has been run, the table still works: missing columns read as their defaults, and the Database sessions tooltip says an upgrade is due. The upgrade only adds what's missing, and (re)creates the triggers and the index. It never drops or rewrites data.
