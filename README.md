# DAISY + Braille Toolkit (WPF, Windows 11)

Dette repo er en WPF-app (Windows 11) der skal gøre det nemt at konvertere Word/TXT til:

- **DAISY-bøger** (fuld tekst + TTS + sync)
- **PEF** (punktskrift til emboss/print)
- **ISO** (til CD / distribution)
- **CSV metadata** (til Epson Total Disc Maker)

## Status lige nu

✅ GUI + job-system med **checkpoints** og **resume** er på plads.
✅ **TTS-cache** er på plads: hvis TTS allerede er lavet for samme tekst+voice+model, genbruges den automatisk.

- Hver kørsel opretter en job-mappe med `job.json`.
- Hvis noget fejler undervejs, kan du trykke **Continue** og den fortsætter ved næste step.
- Hvis noget fejler midt i TTS, kan du køre igen og den fortsætter på næste segment.

### ElevenLabs API key

Du kan enten:

1) Indsætte API key i UI (Password-feltet), eller
2) Sætte en miljøvariabel i Windows:

`ELEVENLABS_API_KEY` (genstart appen bagefter)

## Byg og kør

- Kræver .NET SDK **8.x**
- Åbn solution i Visual Studio og kør.

## Roadmap (næste)

1. Integrér **DAISY Pipeline 2** (Word/TXT → DTBook → DAISY/PEF)
2. Integrér **ElevenLabs** med timestamps/alignment til SMIL sync (SMIL/OPF generation)
3. ISO + CSV (DiscUtils + CsvHelper)

