# Copilot Instructions

## General Guidelines
- Foretræk svar på dansk i fremtidige svar.
- Kode- og repository-filer skal være på engelsk.
- Præsentér muligheder som A/B/C/D eller 1/2/3/4 i stedet for multiple ja/nej spørgsmål.

## Code Style
- Brug specifikke formateringsregler.
- Følg navngivningskonventioner.

## Project-Specific Rules
- Projekt: DAISY-Braille Toolkit, WPF .NET 8-app ved hjælp af ElevenLabs TTS.
- Understøtter flersproget brugergrænseflade med engelsk som standard og dansk som et valgfrit sprog.
- Håndter DAISY/PEF-pipeline og gem metadata og produktioner.
- Integrer SharePoint-lister (DBT_Counters, DBT_Productions, opslaglister) og dokumentbibliotek til metadata og filopbevaring.
- Brugeren vil give SharePoint-webstedets detaljer og ønsker at fortsætte med at opbygge integration.
- Godkendelse bruger MSAL delegeret login (PublicClient).
- Foretræk at bruge den aktuelt logged-in Windows-bruger til SharePoint-operationer (filer og lister), når det er muligt; prioritér SSO med Windows-konto.
- Brugeren arbejder hos Dansk Blindesamfund og kræver, at appen bruger den aktuelt logged-in Windows-bruger (SSO) til SharePoint-operationer.
- Brugeren skal muligvis generere lydfiler, der er kompatible med telefontjenester, og vil senere kontrollere de nødvendige formater for at generere disse lydfiler.
- Arbejdsområde: repo på D:\Github Repoet\daisy-braille-toolkit på branch 'main' (remote origin https://github.com/thebonden/daisy-braille-toolkit).