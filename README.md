# DaikonDock

Hold **Ctrl** and a launcher bar slides out right above the Windows taskbar, with the same size as the taskbar.
Click an icon to start the app. Hold Ctrl again, or press the "∨" button at the right end of the bar, to hide it.
The key to hold can be changed with `triggerKey` (Ctrl, Shift, Alt or Win).

## Build (nothing to install)

1. Double-click `build.bat`
2. Choose the language (`[1] English` / `[2] Japanese`)
3. `dist\DaikonDock.exe` is created, together with sample settings in `dist\data\` and language files in `dist\lang\`
4. Put the `dist` folder anywhere you like (e.g. `C:\Tools\DaikonDock`) and run `DaikonDock.exe`.
   When the icon appears in the notification area, DaikonDock is running.

The build uses the C# compiler (csc.exe) that ships with .NET Framework 4 on Windows 10 / 11, so Visual Studio is not
required. Running `build.bat` again never overwrites the files in `dist\data\` and `dist\lang\`.

To build without the prompt: `build.bat /lang:en` or `build.bat /lang:ja` (add `/nopause` to skip the final pause).

## Usage

| Action                                   | Result                                                             |
|------------------------------------------|--------------------------------------------------------------------|
| Hold Ctrl (`triggerKey`) for about 0.6 s | Show / hide the bar                                                |
| Click an icon                            | Start the app (the bar hides automatically)                        |
| "∨" button at the right end              | Hide the bar                                                       |
| Right-click the bar or the tray icon     | Menu (version, reload, open data folder, start with Windows, exit) |

Holding the key together with another key (e.g. Ctrl+C), or clicking / scrolling while holding it, does not toggle the
bar. When Alt or Win is used, releasing it after the bar appears does not open the menu bar or the Start menu.

## Settings `data/*.json`

All `.json` files directly under the `data` folder next to `DaikonDock.exe` are loaded in file name order. One
`apps.json` is fine, and so is splitting them like `10_work.json` and `20_hobby.json`. Changes are applied the next time
the bar is shown.

```json
{
  "$schema": "./apps.schema.json",
  "settings": {
    "holdMs": 600
  },
  "apps": [
    {
      "name": "Notepad",
      "path": "notepad.exe"
    },
    {
      "name": "Chrome",
      "path": "C:/Program Files/Google/Chrome/Application/chrome.exe"
    },
    {
      "separator": true
    },
    {
      "name": "Work folder",
      "path": "%USERPROFILE%\\Documents\\work"
    },
    {
      "name": "Intranet",
      "path": "https://example.com",
      "icon": "data/icons/site.png"
    }
  ]
}
```

With the `"$schema"` line, VS Code and Rider offer completion and underline mistakes. (`apps.schema.json` itself is not
loaded as a settings file. Its descriptions are in Japanese.)

### apps

| Key         | Required | Description                                                                                             |
|-------------|----------|---------------------------------------------------------------------------------------------------------|
| `path`      | yes      | exe, shortcut (.lnk), folder, file, URL, `ms-settings:`, etc. A bare name like `notepad.exe` also works |
| `name`      |          | Name shown when hovering                                                                                |
| `args`      |          | Command-line arguments                                                                                  |
| `workdir`   |          | Working directory (default: the folder of the exe)                                                      |
| `icon`      |          | Custom icon: `.png`, `.ico`, `.exe`, or an indexed resource like `shell32.dll,4`                        |
| `admin`     |          | `true` to run as administrator                                                                          |
| `separator` |          | `true` for a separator line                                                                             |

- In JSON, write `\` as `\\`. `/` also works.
- Environment variables such as `%USERPROFILE%` can be used. Relative paths are relative to the folder of DaikonDock.exe.
- Comments and trailing commas are not allowed in JSON. If loading fails, a dialog shows the error.
- If no icon can be obtained, the first letter of the name is shown.

### settings (all optional)

| Key                                                  | Default             | Description                                                                                                                          |
|------------------------------------------------------|---------------------|--------------------------------------------------------------------------------------------------------------------------------------|
| `language`                                           | chosen in build.bat | Display language: `en` / `ja` (a file name in the `lang` folder)                                                                     |
| `triggerKey`                                         | "ctrl"              | Key to hold: `ctrl` `shift` `alt` `win`, or only the left / right one: `lctrl` `rctrl` `lshift` `rshift` `lalt` `ralt` `lwin` `rwin` |
| `holdMs`                                             | 600                 | How long the key must be held, in milliseconds                                                                                       |
| `animationMs`                                        | 180                 | Animation duration (0 = no animation)                                                                                                |
| `hideOnLaunch`                                       | true                | Hide the bar after starting an app                                                                                                   |
| `align`                                              | "center"            | `left` / `center` / `right`                                                                                                          |
| `iconSize`                                           | 24                  | Icon size in pixels at 100% scaling                                                                                                  |
| `opacity`                                            | 1.0                 | Opacity (0.2 to 1.0)                                                                                                                 |
| `background` `hover` `pressed` `border` `foreground` | dark theme          | Colors (`#202020` format)                                                                                                            |

## Languages `lang/*.json`

The texts shown by DaikonDock (menu, tooltips, error dialogs) are in `lang\en.json` and `lang\ja.json`.

- The language chosen in `build.bat` is the default. Change it later with `"language"` in `settings`.
- You can edit the texts in `dist\lang\`, or add a language by creating e.g. `dist\lang\fr.json` and setting
  `"language": "fr"`. Changes to language files are applied with "Reload settings" in the menu.
- Missing keys fall back to English. The files are also embedded in the exe, so DaikonDock works even without the
  `lang` folder.
- Detailed error messages that come from Windows (e.g. "file not found") are in the Windows display language.

## Notes

- While an app running as administrator is in the foreground, Windows does not let DaikonDock detect the key. Run
  DaikonDock as administrator if you need this.
- The bar follows the taskbar of the primary monitor (top, left and right taskbars are supported too).
- Items that do not fit in the bar are not shown, and a warning dialog tells you how many. Each icon takes a square as
  wide as the taskbar is tall (about 39 icons on a 1920 px wide screen at 100% scaling).

---

## For developers

### Layout

```
├─ build.bat                Official build for users (csc.exe / C# 5) -> dist\
├─ README.md                This file
├─ LICENSE.md               MIT License
├─ DaikonDock.sln           For Rider
├─ Directory.Build.props    Puts obj / bin under build\
├─ .editorconfig            Coding conventions and analyzer rules
├─ docs/DESIGN.md           Design notes (in Japanese)
├─ data/apps.schema.json    JSON schema for the settings files (copied to dist\data)
├─ lang/                    UI texts (en.json, ja.json)
├─ data/en, data/ja         Sample apps.json per language, copied to dist\data by build.bat
└─ src/
    ├─ DaikonDock.csproj    For Rider (LangVersion=5)
    ├─ AssemblyInfo.cs      Version and product information
    ├─ Program.cs           Entry point, single-instance check
    ├─ AppController.cs     Tray, menu, wiring of the parts
    ├─ BarForm.cs           Bar window: layout, animation, mouse
    ├─ BarForm.Paint.cs     Bar drawing
    ├─ Config.cs            Settings types and JSON loading
    ├─ Strings.cs           UI texts (lang files)
    ├─ HoldKeyDetector.cs   Long-press detection of the trigger key
    ├─ IconLoader.cs        Icon extraction
    ├─ Launcher.cs          Starting apps, path resolution
    ├─ NativeMethods.cs     Win32 API declarations
    └─ Resources/
        ├─ app.png          Source image of the app icon (256 x 256)
        └─ app.ico          App icon made from app.png (embedded in the exe and used for the tray)
```

### Rules

- **Write C# 5 only.** Users build with the csc.exe bundled with Windows. The csproj uses `LangVersion=5`, so newer
  syntax is an error in Rider.
- **No external packages.** Use only what .NET Framework 4.8 provides. (ReferenceAssemblies in the csproj is only for
  building in Rider and is not shipped.)
- **Every UI text goes through `Strings`.** When adding a key, add it to every file in `lang\`.
- Adding a `.cs` file does not require changing build.bat (everything under `src` is compiled).
- Rider builds in English by default. Set `DaikonDockLang` to `ja` in the csproj (or `dotnet build -p:DaikonDockLang=ja`)
  for the Japanese default.
- **Version:** update the three version attributes in `src\AssemblyInfo.cs` for each release. The version is
  shown in the exe properties and at the top of the tray menu.
- Make sure `build.bat` succeeds before committing.

### Static checks

| Method                                               | What it checks                                                                                              |
|------------------------------------------------------|-------------------------------------------------------------------------------------------------------------|
| Rider / `dotnet build`                               | C# 5 syntax, .NET analyzers (latest-recommended), .editorconfig styles. All warnings are errors in Release  |
| `dotnet format --verify-no-changes`                  | Formatting matches .editorconfig                                                                            |
| `jb inspectcode DaikonDock.sln -o=build/inspect.sarif` | Same inspections as Rider, from the command line (`dotnet tool install -g JetBrains.ReSharper.GlobalTools`) |
| `build.bat`                                          | Final check that it builds on a user's machine                                                              |
