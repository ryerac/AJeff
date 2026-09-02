# Icon Packs

Each immediate subdirectory is an icon pack. A pack contains a `pack.json`
manifest and one or more category directories containing square PNG or JPEG files.

Icon IDs are derived from their path:

```text
<pack id>/<category>/<filename without extension>
```

For example, `Core/Audio/toggle-mute.png` has the icon ID
`core/audio/toggle-mute`.

Use unrotated square images, ideally 256x256. PNG is preferred when transparency
is useful; JPEG is also supported. JeffDock handles device-specific resizing,
JPEG encoding, and rotation.
