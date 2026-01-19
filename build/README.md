# Publish / pakke (Windows)

1. Åbn en terminal i repo-roden.
2. Kør:

```powershell
.\build\publish-win-x64.ps1
```

Det laver:
- `dist\win-x64\` (publish-output)
- `dist\DaisyBrailleToolkit-win-x64.zip`

## Ekstra værktøjer og filer
Hvis du lægger filer i disse mapper, bliver de automatisk kopieret med i output/publish:
- `DAISY-Braille Toolkit\Tools\...`
- `DAISY-Braille Toolkit\Scripts\...`
- `DAISY-Braille Toolkit\Data\...`

Tip: Læg fx `dp2.exe` i `DAISY-Braille Toolkit\Tools\`.
