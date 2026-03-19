# STS2 Mod Manager

Simple Windows mod manager for Slay the Spire 2.

## What the program does

This tool helps you manage mods without manually moving folders around.

- Shows which mods are currently enabled in the `mods` folder.
- Shows which mods are disabled in a separate disabled-mods folder.
- Lets you enable or disable a mod by moving its folder between those locations.
- Can import mod `.zip` archives by drag-and-drop or by opening the program with archive paths.
- Detects duplicate mod IDs and lets you choose whether to keep the existing mod or replace it.
- Can restart Slay the Spire 2 through Steam.
- Includes a save manager for moving save data between vanilla and modded save slots.
- Supports English and Simplified Chinese.

The app tries to find your Slay the Spire 2 install automatically by checking parent folders, Steam libraries, and common install paths.

## Release versions

Two executable variants are built:

- `ModManager.FrameworkDependent.exe`
- `ModManager.NativeAot.exe`

### Framework-dependent release

Use this if you already have the required .NET Desktop Runtime installed.

- Smaller download size.
- Depends on the matching .NET runtime being present on the PC.
- Easier to rebuild and debug during development.

### Native AOT release

Use this if you want the simplest standalone executable for end users.

- Self-contained Windows executable.
- Does not require the .NET runtime to be installed separately.
- Usually starts faster.
- Typically produces a larger file.

## Which one should I use?

- For sharing with most players: use `ModManager.NativeAot.exe`.
- For your own machine if you already use modern .NET tools: `ModManager.FrameworkDependent.exe` is fine.

## Build

Run the build script from this folder:

```bat
STS2ModManager.build.cmd
```

Optional build modes:

```bat
STS2ModManager.build.cmd framework
STS2ModManager.build.cmd aot
STS2ModManager.build.cmd all
```