# Jackie's Dalamud Plugins

One repository link for everything I've written or forked.

## Install

In game, open Dalamud settings (`/xlsettings`) → **Experimental** → **Custom Plugin Repositories**,
paste this URL, hit **+**, then **Save**:

```
https://raw.githubusercontent.com/OmegaJackie/DalamudPlugins/main/pluginmaster.json
```

Everything below then shows up in the normal plugin installer.

## What's in here

| Plugin | What it does |
|---|---|
| **Relicable** | Automates the ARR Zodiac relic line. Early alpha, needs an access code. |
| **CineSync** | Self-hosted synced in-world media screens for movie nights. |
| **PlayerTrack** | Keep a record of who you meet and the content you played together. Fork of [Infiziert90/PlayerTrack](https://github.com/Infiziert90/PlayerTrack) with an IPC surface. |
| **ChatAnywhere** | Read and send FFXIV chat from a web browser. Fork of [twelvehouse/ChatAnywhere](https://github.com/twelvehouse/ChatAnywhere). |
| **Bozja Buddy Reborn** | Orchestrates Critical Engagements on the Bozjan Southern Front and Zadnor. |
| **Line Me Up** | Walks you onto your target's spot and matches the way they're facing. |
| **SellJunk** | One click to clear vendor-replaceable clutter out of your bags. |
| **AutoFATE** | Automated FATE grinding — RSR fights, BossMod dodges, vnavmesh walks. Rebuild of the Fate Tool Kit module from [Jaksuhn/ffxiv-bundleoftweaks](https://github.com/Jaksuhn/ffxiv-bundleoftweaks). |

Some of these automate movement and combat, which is against the FFXIV User Agreement and gets
accounts suspended. Each plugin says so on its own page. Use them knowing that.

## How the repo is wired

Two hosting styles, because two of these already have their own release pipelines:

- **Relicable** and **CineSync** live in their own repos and ship through their own GitHub Releases.
  This repo only advertises them — the download link points at their releases, and their metadata is
  pulled live from their own `repo.json` at generation time, so each stays its own source of truth.
  (CineSync's zip is ~92 MB, which is well past what belongs in git anyway.)
- **Everything else** is built here and committed to `dist/`, served over raw links.

Source for the first-party plugins lives in this repo under `plugins/`: **Bozja Buddy Reborn**,
**Line Me Up**, **SellJunk** and **AutoFATE**. The two forks keep their own repos so they can still
pull from upstream: [PlayerTrack-IPC](https://github.com/OmegaJackie/PlayerTrack-IPC) and
[ChatAnywhere-FL](https://github.com/OmegaJackie/ChatAnywhere-FL).

Bozja Buddy Reborn's ECommons `ProjectReference` resolves outside this repo (to
`../../../ZodiacRedone/ECommons`), so a fresh clone won't build that one plugin without that
checkout alongside it. The others are self-contained.

```
plugins/            source for the plugins written here
dist/               built zips, served over raw links
tools/sources.json  the file you edit
pluginmaster.json   generated, never hand-edited
```

`pluginmaster.json` is generated, not hand-edited. `tools/sources.json` is the file you actually
change.

## Updating

```powershell
.\tools\Update-PluginMaster.ps1
```

That rebuilds every locally-hosted plugin, copies each packaged zip into `dist/`, re-reads the
version and description from the manifest DalamudPackager produced, refreshes the two external
entries from their repos, and rewrites `pluginmaster.json`. Add `-Push` to commit and push in the
same step, or `-SkipBuild` to just regenerate the manifest from existing build output.

Because the version is read from the build rather than typed in, the advertised version can't drift
from the shipped binary — which is the usual cause of a plugin that shows a pending update forever.

## Adding a plugin

Add an entry to `tools/sources.json` and re-run the script.

```jsonc
{
  "InternalName": "MyPlugin",              // must match the plugin's InternalName exactly
  "Hosted": "dist",                        // build here and commit the zip
  "Project": "C:\\path\\to\\MyPlugin.csproj",
  "RepoUrl": "https://github.com/...",     // optional, defaults to this repo
  "CategoryTags": [ "utility" ]
}
```

For one that has its own repo and releases, use `"Hosted": "external"` with a `Manifest` URL
pointing at that repo's `repo.json` and a `ReleaseRepo` of `owner/name`.

## Licensing

Licensed per plugin rather than repo-wide, because the forks carry their upstream authors'
copyright and this repo also serves their built binaries.

Relicable and CineSync are original work; Relicable vendors NightmareXIV's ECommons (MIT) inside
its own repo, which is that library's license, not Relicable's.

| Plugin | License | Copyright |
|---|---|---|
| Bozja Buddy Reborn, Line Me Up, SellJunk | MIT — see `plugins/<name>/LICENSE` | OmegaJackie |
| PlayerTrack | MIT | Infi, kalilistic — see [upstream](https://github.com/Infiziert90/PlayerTrack) |
| Relicable | AGPL-3.0 | OmegaJackie |
| CineSync | none stated | OmegaJackie |
| ChatAnywhere | none stated upstream | [twelvehouse](https://github.com/twelvehouse/ChatAnywhere) |
| AutoFATE | BSD 3-Clause — see `plugins/AutoFATE/LICENSE.md` | Puni.sh — rebuild of [Jaksuhn/ffxiv-bundleoftweaks](https://github.com/Jaksuhn/ffxiv-bundleoftweaks) |
