# Installing Godot for this project

Godot is needed only for the visual client: `./dev replay <scenario> --godot` and opening `src/Twin.Client` in the
editor. Tests, `./dev check` and the headless runner need only the .NET SDK.

## Which Godot

**Godot 4.7.2, .NET edition** (the version is pinned in `.godot-version`). The standard edition cannot run the C#
client. Download it from <https://godotengine.org/download/archive/4.7.2-stable/>.

## Where `./dev` looks for it

In this order:

1. The `GODOT` environment variable, if set.
2. `godot` on `PATH`.
3. macOS only: `/Applications/Godot_mono.app` or `~/Applications/Godot_mono.app`.

If Godot is somewhere else, set `GODOT`. It can be:

- a full path to the executable;
- `~/...` for a path under your home directory;
- a path relative to the directory you run `./dev` from;
- on macOS, the `.app` bundle itself (the executable inside it is found for you).

## Per platform

**macOS.** Unzip and move `Godot_mono.app` to `/Applications` (or `~/Applications`). Nothing else is needed. Elsewhere:

```sh
echo 'export GODOT="$HOME/Tools/Godot_mono.app"' >> ~/.zshrc   # then open a new terminal
```

The first time, macOS may refuse to open an app downloaded from the internet: open it once from Finder
(right click, Open) and confirm.

**Windows.** Unzip to a folder of your choice, e.g. `C:\Tools\Godot`. Keep the `GodotSharp` folder next to the `.exe`:
the .NET edition does not work without it. Then set `GODOT` (and open a new terminal):

```bat
setx GODOT "C:\Tools\Godot\Godot_v4.7.2-stable_mono_win64.exe"
```

**Linux.** Unzip, keep the `GodotSharp` folder next to the executable, and either link it onto `PATH` as `godot`
or set `GODOT`:

```sh
echo 'export GODOT="$HOME/Tools/Godot/Godot_v4.7.2-stable_mono_linux.x86_64"' >> ~/.bashrc
```

## Check it

```sh
./dev setup        # prints "Godot 4.7.2.stable.mono... OK" when it is found
./dev replay st1 --godot
```

If Godot cannot be started, `./dev` prints the path it tried and stops. The usual causes: `GODOT` points to the wrong
place, the standard (non-.NET) edition was installed, or the `GodotSharp` folder was not kept next to the executable.
