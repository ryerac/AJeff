# JeffDock Presets

`core.json` defines the action presets bundled with JeffDock. Presets compose
registered, typed action IDs; JSON does not execute arbitrary commands.

Users can add or replace sections and presets without changing the application by
creating `%AppData%\JeffDock\presets.json` with the same schema. A user preset
with the same stable `id` replaces its bundled counterpart. Other presets are
merged into the palette.

Supported control types are `Button` and `Encoder`. Supported binding triggers
are `Press` for either control and `Turn` for encoders. An optional `iconMode`
may be `Static` or `Dynamic`.

Static presets may specify `iconId`, `iconForeground`, and `iconBackground`.
The icon ID refers to any bundled icon-library item, so presets remain small and
do not duplicate image data.
