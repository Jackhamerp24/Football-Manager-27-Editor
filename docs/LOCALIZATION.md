# Interface localization

English strings are in `src/Fm27Editor.App/Resources/Strings.resx`; Vietnamese strings are in `Strings.vi.resx`. Use a stable, context-specific resource key and add the same key to both files. New UI should call `AppLanguage.Get("key")`, rather than using translated text for program logic. If a language has no translation, the resource manager falls back to English.

Some existing shared controls still receive English labels from their callers. `LegacyLabels` maps those labels to resource keys as a temporary migration adapter. Migrate callers to explicit keys incrementally, particularly when the same English word has different meanings. Do not translate names or other data read from the game. Language changes are saved in Settings and take effect on restart.

Run `dotnet test Fm27Editor.slnx -c Release` after changing resources; the app tests check key parity, fallback, and persistence. Adding a new language also requires registering it in `AppLanguage` and the Settings selector.
