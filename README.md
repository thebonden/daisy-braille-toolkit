# DAISY + Braille Toolkit (WPF, Windows 11)

Dette repo er en WPF-app (Windows 11) der skal gøre det nemt at konvertere Word/TXT til:

- **DAISY-bøger** (fuld tekst + TTS + sync)
- **PEF** (punktskrift til emboss/print)
- **ISO** (til CD / distribution)
- **CSV metadata** (til Epson Total Disc Maker)

## Status lige nu

✅ GUI + job-system med **checkpoints** og **resume** er på plads.

- Hver kørsel opretter en job-mappe med `job.json`.
- Hvis noget fejler undervejs, kan du trykke **Continue** og den fortsætter ved næste step.
- Senere tilføjer vi TTS-cache så ElevenLabs ikke bliver kaldt igen, hvis output allerede findes.

## Byg og kør

- Kræver .NET SDK **8.x**
- Åbn solution i Visual Studio og kør.

## Roadmap (næste)

1. Integrér **DAISY Pipeline 2** (Word/TXT → DTBook → DAISY/PEF)
2. Integrér **ElevenLabs** med timestamps/alignment til SMIL sync
3. ISO + CSV (DiscUtils + CsvHelper)

