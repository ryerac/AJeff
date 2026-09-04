# Elgato Icons

This directory contains the large (`svg/l`) variants from
[elgatosf/icons](https://github.com/elgatosf/icons), pinned at commit
`c9fd1bb0a2a986cd2b1098ec09377b0fa17e7f5a` (package version 2.5.1).

The assets are bundled so the JeffDock icon library works without an internet
connection. They are licensed under the MIT License; see `LICENSE.txt` in this
directory.

## Updating the upstream snapshot

From the repository root, run:

    .\scripts\update-elgato-icons.ps1

The script requires Git and internet access. It shallow-clones the latest
`elgatosf/icons` revision, then replaces the bundled `General` icons and updates
`LICENSE.txt`, `pack.json`, and this README with the new version and commit.

After updating, review the upstream changes before committing them:

    git diff --stat
    git diff -- JeffDock.App/Assets/Icons/Packs/Elgato/pack.json
    dotnet build .\JeffDock.App\JeffDock.App.csproj

Treat the files in this directory as a vendored upstream snapshot. Manual
changes may be overwritten the next time the update script runs.
