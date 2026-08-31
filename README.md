# Bloody Mess

A GTA V gore mod. More blood comes out of people, the blood stays on the world, and you
track it out of the puddles on your shoes.

Pure SHVDN script — one `.dll`, one `.ini`, one data file. No asset replacement, no RPF
edits, no `gameconfig`, no dependencies beyond ScriptHookVDotNet itself. It runs on GTA V
Legacy and Enhanced from the same build, because both ship the identical
`ScriptHookVDotNet3.dll`.

## What it does

**Shooting people is messier.** Every hit stamps extra wound decals on the body, fires the
game's own blood particle effects at the entry and exit, and throws a cone of splatter out
the far side that lands on whatever is actually behind them — a wall, a car, the road.
Shotguns and rifles throw more of it than pistols do; a graze throws almost none. Kill
somebody at arm's length and it comes back onto you.

**Bodies bleed out.** A pool spreads from under anybody who goes down, growing over about
half a minute, wet on tarmac and soaked-in on grass. Somebody wounded and still walking
leaves a trail of drops behind them — spaced by distance covered, so standing still leaves
nothing and nobody bleeds forever.

**And you walk it around.** Step in wet blood and it comes with you: a fading trail of
footprints, left and right, pointing the way you are walking, thinning out over the next
dozen paces until you have walked it off. It works for NPCs too, so people fleeing a scene
track it out with them, and for tyres, so driving through a pool leaves marks down the road.

There is an in-game settings menu on **F10**, and every system can be switched off on its
own.

## Installing

Requires **ScriptHookV** and **ScriptHookVDotNet 3**.

Drop the contents of the release zip over your GTA V folder. That gives you:

```
scripts\BloodyMess.dll
scripts\BloodyMess.ini
scripts\BloodyMess\gore.json
```

## Tuning it

`BloodyMess.ini` is commented line by line and is the file to edit. The one setting most
people want is at the top:

```ini
[Intensity]
Level=Mess     ; Tame, Bloody, Mess or Abattoir
```

The in-game menu (F10) changes the same settings live and writes them back to the ini
without disturbing the comments.

`BloodyMess\gore.json` decides what each kind of weapon does — which particle effect, which
wound decal, which of the game's built-in damage packs, and how hard it sprays. The effect
names in it are the game's own; a name the game does not know simply draws nothing.

## Read this before turning it up

**The game has a fixed decal pool**, and script decals compete with the game's own bullet
holes, tyre marks and scuffs for it. Going over that limit does not crash and does not log
anything — decals just stop appearing, or the engine starts recycling ones you wanted to
keep. Both look like this mod being broken rather than this mod being greedy.

So everything this mod draws goes through one ledger with a hard cap, a per-second rate
limit and a range limit, and it evicts its own oldest decal rather than letting the engine
choose. That is the `[Budget]` section, and it is deliberately **not** scaled by the gore
level — turning the gore up cannot turn the safety off.

The defaults are chosen to be survivable on an install with no decal-limit patch at all. If
you are on **Enhanced with DecalPatch** (`DecalPatch.ini`, `Level=4` is 2048 decals), you
have a lot of headroom and `MaxSplatters` can go a long way up.

## Why it is built this way

The gore mod this was written to replace shipped **no configuration at all**. Its bleed loop
was hardcoded — every ped and the player bled continuously, forever, from the moment they
were scratched — so the only way to turn it down was to delete the dll. That is the single
biggest design constraint here:

- Every system is separately switchable, from the ini or from the menu.
- Bleeding runs on a **window that expires**, refreshed by being hurt. Nobody bleeds because
  they once were shot.
- Drips and footprints are paced by **distance travelled**, not by time, so a ped standing
  still produces nothing.
- The engine settings the mod changes are **handed back** on shutdown and on a script reload.

Blood already on the ground is deliberately left alone on reload. A scene you made is yours;
the menu has a "clear all blood now" row for when you want it gone.

## Building

```powershell
.\build.ps1                 # compile to .\build\BloodyMess.dll
.\build.ps1 -Deploy         # ...and install into both GTA V editions
.\build.ps1 -Package        # ...or build a release zip in .\release\
```

The build drives a self-contained Roslyn `csc.exe` out of `tools\` rather than using
`dotnet build`. See `tools\README.md` for why, and for the one command that restores it —
those binaries are not committed.

## Credits

Built from scratch. The decal type numbers and particle effect names are the game's own,
taken from the public native reference rather than from anybody's mod.
