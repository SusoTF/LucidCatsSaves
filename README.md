# Lucid Cats – Save Files

Save files for **Lucid Cats**. Leave a run whenever you want and continue it later, right where you left off: same night, same money, same upgrades. Works solo and in co-op.

<img width="1920" height="1080" alt="20260927150822_1" src="https://github.com/user-attachments/assets/fbaec232-e161-4ea6-9a89-c36b8fca859b" />

## Features

- **Automatic saving**: after every night you survive, and after every purchase in the hall.
- **Load Game** button in the main menu, right below Host, with all your saves. Each one shows:
  - its name
  - the next night you'll play and the group's money
  - the last players on that save
  - when it was created and last saved
  - the game version it was saved with (highlighted if it doesn't match yours)
- **Rename your saves**: they start as "Save 1", "Save 2"... Click a name to rename it.
- **Delete saves**, with a confirmation so you don't lose one by accident.
- **Up to 5 saves.** If all slots are full when you press Host, you get a warning with the choice to host anyway (without saving) or cancel.
- **Co-op support**: each player's money and upgrades are saved separately. Friends who were in the save get their progress back as soon as they join.
- A small **"Saving... / Game saved"** message in the corner every time the game is saved (can be turned off).
- Fits right into the main menu, with the same style, animations and sounds as the game. Menu panels never overlap, and it works alongside other mods like [Bestiary](https://github.com/SusoTF/LucidCatsBestiary).

## How it works

- **When saves are created**: a save is created the first time you survive night 1. Before that, there's no progress to save.
- **Quitting mid-night**: when you load, you're back in the hall before that night, with everything you had at the last save. Anything earned during the unfinished night is lost.
- **Losing a run**: if you lose a night for any reason, the run is over, just like in the base game, so its save is deleted.
- **Loading a game**: press **Load**. You arrive in the hall with everything restored, and friends who were in the save get their progress back when they join.
- **Invite your friends before pressing "Start game"**: the game itself closes the lobby to new players once the run starts.
- **Purchases before starting**: anything you do in the hall before pressing "Start game", like buying upgrades, is kept.

## Multiplayer

- **Only the host needs the mod** for saving and loading. Every player's money and upgrades are restored even if they don't have it.
- **Recommended for everyone**: in a loaded game, the money display at the top of the screen only appears for players who have the mod. Without it, it appears after the first night, like it does in a new game. Their money and upgrades are still restored either way.
- **Saves are stored on the host's PC.** Each save contains every player's data, so if a friend wants to host it, just send them the save file (see below) and everyone gets their progress back.
- **Players are recognised by their Steam account**, so they keep their progress even if they change their Steam name. Steam accounts are only used for this and are never shown.
- **Players who don't come back** to a session keep their data in the save for the next time.
- **New players** who weren't in the save start fresh.

## Requirements

- Lucid Cats (Steam, Windows)
- [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) (tested with 5.4.23.5, x64)

## Installation

1. **Install BepInEx 5** (skip this if you already have it):
   - Download `BepInEx_win_x64_5.4.23.x.zip` from the [BepInEx releases page](https://github.com/BepInEx/BepInEx/releases). Use version 5, not 6.
   - Extract it into the game folder, next to `LucidCats.exe`.
   - Launch the game once and close it.
2. **Download the mod** from the [Releases](../../releases) page.
3. **Extract it into the game folder.** The mod should end up at:
   `BepInEx\plugins\LucidCatsSaves\LucidCatsSaves.dll`
4. Launch the game. The **Load Game** button will be in the main menu, right below Host.

> **Where's the game folder?** In Steam, right-click Lucid Cats → Manage → Browse local files.

## Where are my saves?

Each save is a small file in:

`BepInEx\config\LucidCatsSaves\`

- **Back up** your saves by copying this folder.
- **Share a save** with a friend by sending them the file. They put it in the same folder on their PC.
- **Delete a save** by removing its file, or from the Load Game menu.

## Configuration

After launching the game once with the mod, you can edit `BepInEx\config\lucidcats.savefiles.cfg` with any text editor:

| Setting | Default | What it does |
|---|---|---|
| `ShowSaveIndicator` | `true` | Shows the "Saving... / Game saved" message when the game is saved. |

## Uninstall

Delete the `BepInEx\plugins\LucidCatsSaves` folder. Your saves stay in `BepInEx\config\LucidCatsSaves`, so you can delete that folder too if you don't want them anymore.

## Questions that came to my mind

**Does it change the gameplay?**
No. Runs play exactly the same. The mod only remembers your progress and puts it back when you load.

**Is it safe in multiplayer?**
Yes. Everything is done through the game's own systems, and only the host's PC handles saving and loading.

**What happens when the game updates?**
Each save shows the game version it was saved with, so you can tell older saves apart. New upgrades added by the developers are saved automatically. If an update breaks something, check the [Releases](../../releases) page for a new version or open an issue.

**Can I have more than 5 saves?**
Not at the moment. Delete or reuse an old one to free a slot.

## Building from source

1. Install BepInEx in your game folder (the project uses its files).
2. Install the [.NET SDK](https://dotnet.microsoft.com/download).
3. Open `LucidCatsSaves.csproj` and set `<GameDir>` to your game folder.
4. Build in **Release**. The mod is copied to `BepInEx\plugins\LucidCatsSaves` automatically.

## Changelog

### 1.0.0
- First release.

### 1.0.1
- The mod's description and author are now included in its file, so mod managers can show them.

## Credits

- **Sustain**, for helping me test the multiplayer side of the mod.
- The **BepInEx** and **HarmonyX** teams, for the modding tools that make this possible.
- The developers of **Lucid Cats**, for the game.

## License

[MIT](LICENSE).

This is an unofficial fan-made mod. It is not affiliated with or endorsed by the developers of Lucid Cats.
