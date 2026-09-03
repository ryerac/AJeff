# Official JeffDock plugins

Each plugin is an independent project that references `JeffDock.PluginContracts`.
It must not reference `JeffDock.App`. The app discovers built plugins through their
`plugin.json` manifest in its `Plugins` output directory.

Code plugins run in-process and are trusted with the same permissions as JeffDock.
