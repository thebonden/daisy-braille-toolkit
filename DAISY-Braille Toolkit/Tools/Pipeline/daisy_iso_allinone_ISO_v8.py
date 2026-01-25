import os, sys, subprocess, tempfile, requests, shutil, json
from pathlib import Path

__version__ = "v6"


# ===== STANDARD ROOT =====
DEFAULT_ROOT = r"C:\DAISY-BOOKS\originaldokumenter"

def load_json(path: Path):
    """Læs JSON (tåler UTF-8 BOM)."""
    if not path.exists():
        raise FileNotFoundError(f"Mangler fil: {path}")
    with path.open("r", encoding="utf-8-sig") as f:
        return json.load(f)


def load_or_create_secrets(secrets_path: Path) -> dict:
    """Indlæs secrets.json (kun nøgler). Hvis den mangler, så tilbyd at oprette den."""
    if secrets_path.exists():
        return load_json(secrets_path)

    print(f"FEJL: Mangler fil: {secrets_path}")
    print("Jeg kan oprette den for dig nu.")
    # Prøv at skjule input (kan fejle i IDLE), ellers brug almindelig input().
    api_key = ""
    try:
        import getpass
        api_key = (getpass.getpass("Indsæt ELEVEN_API_KEY (skjult input): ") or "").strip()
    except Exception:
        api_key = (input("Indsæt ELEVEN_API_KEY: ") or "").strip()

    if not api_key:
        raise FileNotFoundError(f"Mangler fil: {secrets_path}")

    data = {"ELEVEN_API_KEY": api_key}
    secrets_path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"OK: Oprettede {secrets_path.name} i samme mappe som scriptet.")
    return data

def load_optional_json(path: Path, default):
    """Læs JSON hvis filen findes, ellers returnér default.
    (Tåler UTF-8 BOM)."""
    if not path.exists():
        return default
    with path.open("r", encoding="utf-8-sig") as f:
        return json.load(f)

def _get_voice_id(v: dict) -> str:
    return (v.get("voice_id") or v.get("voiceId") or v.get("id") or v.get("ID") or "").strip()

def normalize_voices_data(raw):
    """Accepterer voices.json i flere formater og returnerer en liste af voice-dicts.

    Understøtter bl.a. din eksport (med 'voice_id') og mere simple lister (med 'id').
    Vi bevarer de originale felter, men sikrer at både:
      - voice_id  (primær)
      - id        (alias)
    findes når muligt.
    """
    if raw is None:
        return []

    # Nogle gemmer som {"voices": [...]}
    if isinstance(raw, dict) and "voices" in raw:
        raw = raw.get("voices")

    if not isinstance(raw, list):
        return []

    out = []
    for item in raw:
        if not isinstance(item, dict):
            continue

        vid = _get_voice_id(item)
        name = (item.get("name") or item.get("display_name") or item.get("voice_name") or "").strip()
        if not name:
            name = "(uden navn)"

        v = dict(item)  # copy
        if vid:
            v["voice_id"] = vid
            v["id"] = vid
        v["name"] = name
        out.append(v)

    return out

def fetch_voices_from_elevenlabs(api_key: str):
    """Hent stemmer fra ElevenLabs API og byg voices.json i *samme* format som dit PowerShell eksport-script."""
    url = "https://api.elevenlabs.io/v1/voices"
    headers = {"xi-api-key": api_key, "accept": "application/json"}
    r = requests.get(url, headers=headers, timeout=30)
    r.raise_for_status()
    data = r.json()

    fetched_on = __import__("datetime").datetime.utcnow().replace(microsecond=0).isoformat() + "Z"

    out = []
    for v in (data.get("voices") or []):
        if not isinstance(v, dict):
            continue

        labels = v.get("labels") or {}
        if not isinstance(labels, dict):
            labels = {}

        voice_id = (v.get("voice_id") or v.get("voiceId") or v.get("id") or "").strip()
        if not voice_id:
            continue

        def lbl(*keys):
            for k in keys:
                if k in labels and labels[k] is not None:
                    val = labels[k]
                    if isinstance(val, list):
                        return ",".join(str(x) for x in val)
                    return str(val)
            return ""

        row = {
            "fetched_on": fetched_on,
            "source_url": url,
            "source_type": "my",
            "name": (v.get("name") or "(uden navn)").strip(),
            "voice_id": voice_id,
            "gender": lbl("gender", "sex"),
            "age": lbl("age"),
            "accent": lbl("accent"),
            "description": v.get("description") or "",
            "use_case": lbl("use_case", "usecase", "use-case"),
            "preview_url": v.get("preview_url") or "",
            "languages": lbl("languages", "language", "lang", "locale"),

            "category": v.get("category") or "",
            "available_for_tiers": ",".join(str(x) for x in (v.get("available_for_tiers") or [])),
            # vi gemmer hele fine_tuning objektet, så du har samme struktur som eksporten
            "fine_tuning_state": (v.get("fine_tuning") or {}).get("finetuning_state")
                                or (v.get("fine_tuning") or {}).get("fine_tuning_state")
                                or (v.get("fine_tuning") or {}).get("state")
                                or {},
            "is_allowed_to_fine_tune": (v.get("fine_tuning") or {}).get("is_allowed_to_fine_tune")
                                       or (v.get("fine_tuning") or {}).get("isAllowedToFineTune"),
            "labels_raw": json.dumps(labels, ensure_ascii=False, separators=(",", ":")),

            "in_my_voices": True,
            "likely_selectable_now": True,
        }
        # alias (til scriptets interne brug)
        row["id"] = row["voice_id"]
        out.append(row)

    return out


def run_powershell_export_voices(script_dir: Path, secrets_path: Path, ps1_name: str = "Export-ElevenLabsVoices_v12_1.ps1") -> bool:
    """Kør dit PowerShell eksport-script for at bygge voices.json (samme format som du har).
    Forventer at ps1 ligger i samme mappe som .py (script_dir).
    """
    ps1_path = script_dir / ps1_name
    if not ps1_path.exists():
        return False

    # Find PowerShell (pwsh eller Windows PowerShell)
    ps_exe = shutil.which("pwsh") or shutil.which("powershell")
    if not ps_exe:
        print("ADVARSEL: PowerShell (pwsh/powershell) ikke fundet, kan ikke køre export-script.")
        return False

    # Kør export-scriptet og bed det genopbygge voices.json i script-mappen
    cmd = [
        ps_exe,
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", str(ps1_path),
        "-SecretsPath", str(secrets_path),
        "-RebuildVoicesJson",
        "-MyVoicesJsonPath", "voices.json",
    ]
    try:
        print(f"Kører PowerShell export for voices.json: {ps1_path.name}")
        subprocess.run(cmd, cwd=str(script_dir), check=True)
        return (script_dir / "voices.json").exists()
    except subprocess.CalledProcessError as e:
        print(f"ADVARSEL: PowerShell export fejlede (exit {e.returncode}).")
        return False
    except Exception as e:
        print(f"ADVARSEL: Kunne ikke køre PowerShell export: {e}")
        return False


def ensure_voices_list(raw_voices, api_key: str, voices_path: Path):
    """Sørger for at vi ender med en brugbar stemmeliste (med voice_id).

    - Hvis voices.json mangler: spørg om vi skal hente den fra ElevenLabs og oprette den.
    - Hvis voices.json findes men mangler voice_id: spørg om vi skal genopbygge den.
    """
    voices = normalize_voices_data(raw_voices)

    def has_ids(vs):
        return [v for v in vs if _get_voice_id(v)]

    voices_with_id = has_ids(voices)
    if voices_with_id:
        if len(voices_with_id) != len(voices):
            print("ADVARSEL: Nogle stemmer i voices.json mangler 'voice_id' og ignoreres.")
        # sørg for alias id/voice_id
        for v in voices_with_id:
            vid = _get_voice_id(v)
            v["voice_id"] = vid
            v["id"] = vid
        return voices_with_id

    # Her er der ingen brugbare id'er (enten mangler filen, eller formatet er forkert)
    script_dir = voices_path.parent
    secrets_path = script_dir / "secrets.json"

    # 1) Prøv (valgfrit) at genopbygge voices.json via dit PowerShell-export script
    ps1_exists = (script_dir / "Export-ElevenLabsVoices_v12_1.ps1").exists()
    if ps1_exists:
        ans = input("voices.json mangler/er ugyldig. Genopbyg via PowerShell export-script? (J/n): ").strip().lower()
        if ans in ("", "j", "ja", "y", "yes"):
            if run_powershell_export_voices(script_dir, secrets_path):
                try:
                    raw2 = load_json(script_dir / "voices.json")
                    voices2 = normalize_voices_data(raw2)
                    voices2_with_id = [v for v in voices2 if _get_voice_id(v)]
                    if voices2_with_id:
                        if len(voices2_with_id) != len(voices2):
                            print("ADVARSEL: Nogle stemmer i voices.json mangler 'voice_id' og ignoreres.")
                        # sørg for alias id/voice_id
                        for v in voices2_with_id:
                            vid = _get_voice_id(v)
                            v["id"] = vid
                            v["voice_id"] = vid
                        return voices2_with_id
                except Exception as e:
                    print(f"ADVARSEL: voices.json blev lavet, men kunne ikke læses: {e}")
            print("ADVARSEL: Kunne ikke genopbygge voices.json via PowerShell. Prøver evt. API i stedet.")

    # 2) Hent via ElevenLabs API og opret voices.json (samme format som eksport-scriptet)
    if raw_voices is None:
        ans = input("Vil du hente stemmer fra ElevenLabs API og oprette voices.json nu? (J/n): ").strip().lower()
        if ans not in ("", "j", "ja", "y", "yes"):
            raise FileNotFoundError("voices.json mangler, og du valgte ikke at hente den automatisk.")
    else:
        ans = input("voices.json findes, men har ingen brugbare 'voice_id'. Hent stemmer igen og overskriv voices.json? (J/n): ").strip().lower()
        if ans not in ("", "j", "ja", "y", "yes"):
            raise RuntimeError("voices.json indeholder ingen brugbare 'voice_id'.")
    print("Henter stemmer fra ElevenLabs API ...")
    voices = fetch_voices_from_elevenlabs(api_key)
    if not voices:
        raise RuntimeError("ElevenLabs returnerede ingen stemmer. Tjek din ELEVEN_API_KEY.")

    # Backup + skriv i samme format som eksport-scriptet
    try:
        if voices_path.exists():
            backup = voices_path.with_suffix(".bak.json")
            shutil.copy2(voices_path, backup)
            print(f"Backup gemt: {backup}")
        voices_path.write_text(json.dumps(voices, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"Skrev ny voices.json med {len(voices)} stemmer: {voices_path}")
    except Exception as e:
        print(f"ADVARSEL: Kunne ikke skrive voices.json: {e}")

    return voices

def pick_directory(title: str, initial: str | None = None) -> Path | None:
    """Prøv at åbne en mappe-vælger. Falder tilbage til None hvis UI ikke virker."""
    try:
        import tkinter as tk
        from tkinter import filedialog
        tk_root = tk.Tk()
        tk_root.withdraw()
        try:
            tk_root.attributes("-topmost", True)
        except Exception:
            pass
        chosen = filedialog.askdirectory(title=title, initialdir=initial or os.getcwd())
        tk_root.destroy()
        if chosen:
            return Path(chosen)
    except Exception:
        return None
    return None


def choose_from_list(title, items, label_fn):
    print("\n" + title)
    for i, item in enumerate(items):
        print(f"[{i}] {label_fn(item)}")

    while True:
        choice = input(f"Vælg nummer (0-{len(items)-1}): ").strip()
        if choice.isdigit():
            idx = int(choice)
            if 0 <= idx < len(items):
                return items[idx]
        print("Ugyldigt valg, prøv igen.")

def choose_file_from_list(title, files):
    print("\n" + title)
    for i, f in enumerate(files):
        print(f"[{i}] {f.name}")
    print("[a] Alle filer")

    while True:
        choice = input("Vælg nummer eller 'a': ").strip().lower()
        if choice == "a":
            return files
        if choice.isdigit():
            idx = int(choice)
            if 0 <= idx < len(files):
                return [files[idx]]
        print("Ugyldigt valg, prøv igen.")

def ensure_tool_exists(cmd):
    # simpel check: pipeline2 skal kunne køres
    try:
        subprocess.run([cmd, "--help"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
    except FileNotFoundError:
        raise FileNotFoundError(
            f"Kan ikke finde '{cmd}'. Hvis den ikke er i PATH, så sæt DAISY_PIPELINE_CMD i secrets.json\n"
            f"fx: C:\\Program Files\\DAISY Pipeline 2\\cli\\pipeline2.bat"
        )

def docx_to_text(input_file: Path, text_file: Path):
    """Konverter .txt/.docx til ren tekst.

    - .txt kopieres
    - .docx læses med python-docx (installeret som 'python-docx')
    - fallback: pandoc hvis docx-læsning fejler
    """
    suf = input_file.suffix.lower()

    if suf == ".txt":
        shutil.copy(str(input_file), str(text_file))
        return

    if suf == ".docx":
        # Først: python-docx (ingen eksterne afhængigheder)
        try:
            import docx  # python-docx

            d = docx.Document(str(input_file))
            paras = []
            for p in d.paragraphs:
                txt = (p.text or "").strip()
                if txt:
                    paras.append(txt)
            # Brug blank linje mellem afsnit
            text_file.write_text("\n\n".join(paras), encoding="utf-8")
            return
        except Exception:
            # Fallback: pandoc hvis det findes
            pass

    # Fallback generelt (kræver pandoc i PATH)
    subprocess.run(["pandoc", str(input_file), "-t", "plain", "-o", str(text_file)], check=True)

def split_paragraphs(content: str):
    paragraphs = [p.strip() for p in content.split("\n\n") if p.strip()]
    return paragraphs

def chunk_text(text: str, max_chars: int = 4800):
    """Split tekst i bidder <= max_chars (forsøger at splitte pænt på sætninger/ord)."""
    text = (text or "").strip()
    if not text:
        return []

    if len(text) <= max_chars:
        return [text]

    chunks = []
    rest = text
    seps = [". ", "! ", "? ", "; ", ": ", ", ", " "]
    while rest:
        if len(rest) <= max_chars:
            chunks.append(rest.strip())
            break

        cut = rest[:max_chars]
        cut_pos = None
        for sep in seps:
            pos = cut.rfind(sep)
            if pos > max_chars * 0.6:  # undgå alt for små bidder
                cut_pos = pos + len(sep)
                break

        if not cut_pos:
            cut_pos = max_chars

        chunk = rest[:cut_pos].strip()
        if chunk:
            chunks.append(chunk)
        rest = rest[cut_pos:].lstrip()

    return chunks


def elevenlabs_tts(api_key: str, voice_id: str, model_id: str, text: str) -> bytes:
    resp = requests.post(
        f"https://api.elevenlabs.io/v1/text-to-speech/{voice_id}",
        headers={
            "xi-api-key": api_key,
            "Content-Type": "application/json",
        },
        json={
            "text": text[:5000],
            "model_id": model_id,
        },
        timeout=120
    )
    resp.raise_for_status()
    return resp.content

def build_simple_daisy(book_name: str, audio_dir: Path, daisy_dir: Path):
    daisy_dir.mkdir(parents=True, exist_ok=True)

    mp3_files = sorted(audio_dir.glob("chapter_*.mp3"))

    # ncc.html (meget simpel)
    ncc_path = daisy_dir / "ncc.html"
    with ncc_path.open("w", encoding="utf-8") as ncc:
        ncc.write(f"<html><head><title>{book_name}</title></head><body>\n")
        for i in range(1, len(mp3_files) + 1):
            ncc.write(f'<h1><a href="chapter_{i:03}.smil">Kapitel {i}</a></h1>\n')
        ncc.write("</body></html>")

    # kopier lyd + lav smil
    for mp3_path in mp3_files:
        dst_mp3 = daisy_dir / mp3_path.name
        shutil.copy(str(mp3_path), str(dst_mp3))

        smil_path = daisy_dir / mp3_path.with_suffix(".smil").name
        with smil_path.open("w", encoding="utf-8") as smil:
            smil.write(f"""<?xml version="1.0"?>
<smil>
  <body>
    <seq>
      <audio src="{mp3_path.name}" clip-begin="0s" />
    </seq>
  </body>
</smil>
""")


def create_iso_via_powershell_imapi(source_dir: Path, iso_path: Path, volume_name: str):
    """Create an ISO from a folder using Windows' built-in IMAPI2 (via PowerShell).

    This avoids needing DAISY Pipeline for ISO creation.
    """
    src = str(source_dir.resolve())
    dst = str(iso_path.resolve())
    vol = (volume_name or "DAISY").strip()
    # ISO volume labels can be picky; keep it simple/portable.
    import re as _re
    vol = _re.sub(r"[^A-Za-z0-9_]", "_", vol).upper() or "DAISY"
    if len(vol) > 32:
        vol = vol[:32]

    def esc_ps(s: str) -> str:
        # Escape single quotes for PowerShell single-quoted strings
        return s.replace("'", "''")

    ps = f"""$ErrorActionPreference='Stop'
$src = '{esc_ps(src)}'
$dst = '{esc_ps(dst)}'
$vol = '{esc_ps(vol)}'

if (-not (Test-Path -LiteralPath $src)) {{ throw "Kilde-mappen findes ikke: $src" }}
$parent = Split-Path -Parent $dst
if ($parent -and -not (Test-Path -LiteralPath $parent)) {{ New-Item -ItemType Directory -Path $parent | Out-Null }}

$fs = New-Object -ComObject IMAPI2FS.MsftFileSystemImage
# 1=ISO9660, 2=Joliet, 4=UDF => 7 = ISO9660+Joliet+UDF (bedst til lange filnavne)
$fs.FileSystemsToCreate = 7
$fs.VolumeName = $vol
# UDF 1.02
try {{ $fs.UDFRevision = 0x102 }} catch {{ }}

$fs.Root.AddTree($src, $false) | Out-Null
$result = $fs.CreateResultImage()
$img = $result.ImageStream

# IMPORTANT: $img is a COM object. In Windows PowerShell, a direct cast to IStream can fail.
# Use IUnknown -> typed object to reliably access System.Runtime.InteropServices.ComTypes.IStream.
$unk = [System.Runtime.InteropServices.Marshal]::GetIUnknownForObject($img)
try {{
    $istream = [System.Runtime.InteropServices.Marshal]::GetTypedObjectForIUnknown(
        $unk, [type]([System.Runtime.InteropServices.ComTypes.IStream])
    )

    $target = [System.IO.File]::Open($dst, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    $ptr = [System.Runtime.InteropServices.Marshal]::AllocHGlobal(4)
    try {{
        $buffer = New-Object byte[] 2048
        while ($true) {{
            $istream.Read($buffer, 2048, $ptr)
            $read = [System.Runtime.InteropServices.Marshal]::ReadInt32($ptr)
            if ($read -le 0) {{ break }}
            $target.Write($buffer, 0, $read)
        }}
    }} finally {{
        [System.Runtime.InteropServices.Marshal]::FreeHGlobal($ptr)
        $target.Close()
    }}
}} finally {{
    if ($unk -ne [IntPtr]::Zero) {{ [System.Runtime.InteropServices.Marshal]::Release($unk) | Out-Null }}
}}
"""

    subprocess.run(
        ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", ps],
        check=True
    )


def create_iso(iso_cmd: str | None, source_dir: Path, iso_path: Path, volume_name: str):
    # If you have a dedicated ISO tool, you can set it in settings.json:
    #   "ISO_CMD": "oscdimg"  (or full path to oscdimg.exe)
    #   "ISO_CMD": "mkisofs"  (or full path)
    # Otherwise we fall back to PowerShell IMAPI2 (built-in).
    cmd = (iso_cmd or "auto").strip()

    # Hvis nogen har sat ISO_CMD til "pipeline2" ved en fejl, så brug auto i stedet
    if cmd.lower() in ("pipeline2", "dp2"):
        print(f"ADVARSEL: ISO_CMD var sat til {cmd!r}. Jeg bruger 'auto' i stedet.")
        cmd = "auto"

    # sanitize volume label
    import re as _re
    volume_name = _re.sub(r"[^A-Za-z0-9_]", "_", (volume_name or "DAISY")).upper() or "DAISY"
    if len(volume_name) > 32:
        volume_name = volume_name[:32]


    def which(name: str) -> str | None:
        return shutil.which(name)

    if cmd.lower() in ("auto", ""):
        if which("oscdimg"):
            cmd = "oscdimg"
        elif which("mkisofs"):
            cmd = "mkisofs"
        elif which("genisoimage"):
            cmd = "genisoimage"
        else:
            cmd = "powershell-imapi2"

    if cmd.lower() in ("powershell-imapi2", "imapi2", "powershell"):
        create_iso_via_powershell_imapi(source_dir, iso_path, volume_name)
        return

    # External tools
    if cmd.lower().endswith("oscdimg") or cmd.lower().endswith("oscdimg.exe") or os.path.basename(cmd).lower() in ("oscdimg", "oscdimg.exe"):
        ensure_tool_exists(cmd)
        # -m ignore max size, -o optimize storage, -u2 UDF, -udfver102, -l volume label, -n allow long names
        subprocess.run([cmd, "-m", "-o", "-u2", "-udfver102", f"-l{volume_name}", str(source_dir), str(iso_path)], check=True)
        return

    if os.path.basename(cmd).lower() in ("mkisofs", "genisoimage") or cmd.lower().endswith(("mkisofs.exe", "genisoimage.exe", "mkisofs", "genisoimage")):
        ensure_tool_exists(cmd)
        subprocess.run([cmd, "-V", volume_name, "-J", "-R", "-o", str(iso_path), str(source_dir)], check=True)
        return

    raise FileNotFoundError(
        f"Ukendt ISO_CMD: {iso_cmd!r}. Brug fx 'auto' eller 'powershell-imapi2', eller en sti til oscdimg/mkisofs."
    )

def process_one_file(input_file: Path, api_key: str, voice_id: str, model_id: str, iso_cmd: str, max_tts_chars: int = 4800):
    input_file = input_file.resolve()
    book_name = input_file.stem
    output_iso = input_file.with_suffix(".iso")

    # midlertidig arbejdsmappe
    work = Path(tempfile.mkdtemp(prefix="daisy_work_"))
    text_file = work / "text.txt"
    audio_dir = work / "audio"
    audio_dir.mkdir(parents=True, exist_ok=True)

    try:
        # DOCX/TXT -> tekst
        docx_to_text(input_file, text_file)

        # læs tekst og split
        content = text_file.read_text(encoding="utf-8").strip()
        paragraphs = split_paragraphs(content)
        # Del meget lange afsnit op, så ElevenLabs ikke truncater (max ~5000 tegn)
        expanded = []
        for p in paragraphs:
            expanded.extend(chunk_text(p, max_chars=max_tts_chars))
        paragraphs = expanded

        if not paragraphs:
            print(f"Ingen tekst fundet i {input_file.name}")
            return

        # lav lyd
        for i, para in enumerate(paragraphs, start=1):
            print(f"[{input_file.name}] Laver lyd {i}/{len(paragraphs)} ...")
            mp3_bytes = elevenlabs_tts(api_key, voice_id, model_id, para)
            out_mp3 = audio_dir / f"chapter_{i:03}.mp3"
            out_mp3.write_bytes(mp3_bytes)

        # lav simpel DAISY-mappe
        daisy_dir = work / book_name
        build_simple_daisy(book_name, audio_dir, daisy_dir)

        # pipeline -> iso
        print(f"[{input_file.name}] Laver ISO ...")
        create_iso(iso_cmd, daisy_dir, output_iso, volume_name=book_name)

        print(f"FÆRDIG: {output_iso}")

    finally:
        # ryd op (kan kommenteres ud hvis du vil debugge)
        shutil.rmtree(work, ignore_errors=True)

def main():
    script_dir = Path(__file__).resolve().parent
    secrets_path = script_dir / "secrets.json"
    settings_path = script_dir / "settings.json"
    voices_path = script_dir / "voices.json"

    # secrets.json: kun nøgler/koder
    secrets = load_or_create_secrets(secrets_path)
    # settings.json: stier og øvrige indstillinger (valgfri fil)
    settings = load_optional_json(settings_path, default={})
    raw_voices = load_optional_json(voices_path, default=None)
    print(f"voices.json sti: {voices_path}")
    if raw_voices is None:
        print("voices.json blev ikke fundet (eller kunne ikke læses).")
    elif isinstance(raw_voices, list):
        print(f"voices.json stemmer: {len(raw_voices)}")
        if raw_voices:
            print("voices.json første element keys:", ", ".join(list(raw_voices[0].keys())[:20]))
    elif isinstance(raw_voices, dict):
        print(f"voices.json er et object med keys: {', '.join(list(raw_voices.keys())[:20])}")

    api_key = (secrets.get("ELEVEN_API_KEY") or "").strip()
    if not api_key:
        print("FEJL: ELEVEN_API_KEY mangler i secrets.json")
        sys.exit(1)


    voices = ensure_voices_list(raw_voices, api_key, voices_path)

    model_id = (settings.get("MODEL_ID") or "eleven_multilingual_v2").strip()
    iso_cmd = (settings.get("ISO_CMD") or "auto").strip() or "auto"
    max_tts_chars = int(settings.get("MAX_TTS_CHARS", 4800) or 4800)
    default_root = (settings.get("DEFAULT_ROOT") or DEFAULT_ROOT)

    # vælg stemme (med genopbyg hvis voices.json mangler ID)
    while True:
        selected_voice = choose_from_list(
            "Vælg stemme:",
            voices,
            lambda v: f"{v.get('name','(uden navn)')} (voice_id: {_get_voice_id(v)})"
        )
        voice_id = (_get_voice_id(selected_voice) or "").strip()
        if voice_id:
            break

        print("\nADVARSEL: Den valgte stemme har ingen voice_id i voices.json.")
        print("Jeg prøver at genopbygge voices.json (PowerShell export / ElevenLabs API) og spørger igen.")
        # Tving gen-check/genopbyg: læs raw igen og kør ensure_voices_list endnu en gang
        try:
            raw_retry = load_json(script_dir / "voices.json") if (script_dir / "voices.json").exists() else None
        except Exception:
            raw_retry = None
        voices = ensure_voices_list(raw_retry, api_key, script_dir / "voices.json")
# vælg projektmappe
    root = Path(default_root)
    if not root.exists():
        print(f"ADVARSEL: Standard root findes ikke: {root}")
        picked = pick_directory("Vælg projekt-root (mappen med dine projektmapper)", initial=str(Path.home()))
        if picked:
            root = picked
        else:
            alt = input("Indtast sti til projekt-root (eller tryk Enter for at afbryde): ").strip().strip('"')
            if alt:
                root = Path(alt)
            else:
                print("Ingen mappe valgt. Afslutter.")
                sys.exit(1)

    if not root.exists():
        print(f"FEJL: Root findes stadig ikke: {root}")
        sys.exit(1)

    folders = sorted([p for p in root.iterdir() if p.is_dir()], key=lambda p: p.name.lower())
    if not folders:
        items = [p.name for p in root.iterdir()]
        if not items:
            print(f"Mappen er tom: {root}")
        else:
            print(f"Ingen undermapper (projektmapper) fundet i: {root}")
            print("Indhold i mappen:")
            for name in sorted(items, key=str.lower):
                print(" -", name)
        sys.exit(1)

    selected_folder = choose_from_list(
        f"Vælg mappe i {root}:",
        folders,
        lambda p: p.name
    )

    files = []
    for ext in ("*.docx", "*.txt"):
        files.extend(selected_folder.glob(ext))
    files = sorted(files, key=lambda p: p.name.lower())

    if not files:
        items = [p.name for p in selected_folder.iterdir()]
        if not items:
            print(f"Mappen er tom: {selected_folder}")
        else:
            print(f"Ingen .docx eller .txt fundet i: {selected_folder}")
            print("Filer der ligger i mappen:")
            for name in sorted(items, key=str.lower):
                print(" -", name)
        sys.exit(1)

    selected_files = choose_file_from_list(
        f"Vælg fil i {selected_folder}:",
        files
    )

    # kør
    for f in selected_files:
        process_one_file(f, api_key, voice_id, model_id, iso_cmd, max_tts_chars=max_tts_chars)

if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        # behold exit-koder, men vis stadig en pause så konsolvindue ikke lukker med det samme
        raise
    except Exception as e:
        print("\nFEJL (uventet):", e)
        import traceback
        traceback.print_exc()
    finally:
        try:
            input("\nTryk Enter for at afslutte...")
        except Exception:
            pass
