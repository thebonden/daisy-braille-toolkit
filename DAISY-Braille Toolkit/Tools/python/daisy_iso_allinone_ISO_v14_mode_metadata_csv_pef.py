
def _choose_writable_output_path(path: Path) -> Path:
    'Try to overwrite path. If locked (e.g., ISO mounted), choose a timestamped name.'
    try:
        if path.exists():
            try:
                path.unlink()
            except PermissionError:
                ts = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
                alt = path.with_name(f"{path.stem}_{ts}{path.suffix}")
                print(f"ADVARSEL: Kan ikke overskrive {path} (låst af en anden proces).")
                print("Tip: Luk Stifinder-preview, unmount ISO, eller slet/omdøb filen og prøv igen.")
                print(f"Jeg skriver ISO til: {alt}")
                return alt
    except Exception:
        ts = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
        alt = path.with_name(f"{path.stem}_{ts}{path.suffix}")
        print(f"ADVARSEL: Problem med output-stien {path}. Jeg skriver ISO til: {alt}")
        return alt
    return path

import os, sys, subprocess, tempfile, requests, shutil, json, csv, html
import datetime
from pathlib import Path

__version__ = "v13-lang-csv-pef"


# ===== STANDARD ROOT =====
DEFAULT_ROOT = r"C:\DAISY-BOOKS\originaldokumenter"


class _TeeTextIO:
    """Skriver både til konsol og til en logfil."""
    def __init__(self, console, logfile):
        self.console = console
        self.logfile = logfile

    def write(self, s):
        try:
            self.console.write(s)
        except Exception:
            pass
        try:
            self.logfile.write(s)
        except Exception:
            pass
        return len(s) if isinstance(s, str) else 0

    def flush(self):
        try:
            self.console.flush()
        except Exception:
            pass
        try:
            self.logfile.flush()
        except Exception:
            pass

def _setup_logfile(script_dir: Path) -> tuple[Path, object]:
    logs_dir = script_dir / "logs"
    logs_dir.mkdir(parents=True, exist_ok=True)
    ts = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
    log_path = logs_dir / f"daisy_run_{ts}.log"
    lf = open(log_path, "w", encoding="utf-8", errors="replace")
    return log_path, lf

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
    - Tåler UTF-8 BOM
    - Hvis JSON er ugyldig, så print en forklaring og fortsæt med default (så du ikke 'betaler' for et helt run)."""
    if not path.exists():
        return default
    try:
        with path.open("r", encoding="utf-8-sig") as f:
            return json.load(f)
    except json.JSONDecodeError as e:
        print(f"ADVARSEL: {path} er ikke gyldig JSON og bliver ignoreret.")
        print(f"  -> {e.msg} (linje {e.lineno}, kolonne {e.colno})")
        print("  Ret filen eller slet den. Eksempel på settings.json:")
        print('  {\n    "USE_TTS_CACHE": true,\n    "CACHE_DIR": "tts_cache"\n  }')
        return default

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



LANG_NAMES = {
    "auto": "Auto (afled af stemme)",
    "da": "Dansk",
    "en": "English",
    "de": "Deutsch",
    "sv": "Svenska",
    "no": "Norsk",
}

def _voice_langs(v) -> list[str]:
    raw = (v.get("languages") or "").strip()
    if not raw:
        return []
    return [x.strip().lower() for x in raw.split(",") if x.strip()]

def choose_language_from_voices(voices):
    langs = sorted({l for v in voices for l in _voice_langs(v)})
    items = ["auto"] + langs
    return choose_from_list(
        "Vælg sprog (bogen er 1-sprogs):",
        items,
        lambda c: f"{c} ({LANG_NAMES.get(c, c)})"
    )

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
            f"Kan ikke finde '{cmd}'. Hvis den ikke er i PATH, så sæt DAISY_PIPELINE_CMD i settings.json\n"
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




def write_segments_csv(chunks: list[str], csv_path: Path):
    """(Valgfrit) Skriv en semikolon-separeret CSV med tekst + mp3-navn pr. afsnit."""
    csv_path.parent.mkdir(parents=True, exist_ok=True)
    with csv_path.open("w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f, delimiter=";")
        w.writerow(["index", "mp3", "chars", "text"])
        for i, t in enumerate(chunks, start=1):
            w.writerow([i, f"chapter_{i:03}.mp3", len(t), t])

def write_metadata_csv(meta: dict, csv_path: Path):
    """Skriv metadata-CSV (én række) efter jeres skabelon."""
    csv_path.parent.mkdir(parents=True, exist_ok=True)
    # fast rækkefølge (matcher 'metadata daisy.txt')
    headers = [
        "Lengte",
        "Stemme",
        "Sprog",
        "Pruduceret for",
        "Titel",
        "Dato for Intale",
        "Tid for intale",
        "Pruduceret af",
        "Daisy vertion",
        "orginaldokomenter navne+format",
        "Afsender",
        "Volume label",
        "Write data"
    ]
    with csv_path.open("w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f, delimiter=";")
        w.writerow(headers)
        w.writerow([meta.get(h, "") for h in headers])

def _format_hhmmss(total_seconds: float) -> str:
    try:
        total_seconds = max(0, int(round(float(total_seconds))))
    except Exception:
        return ""
    h = total_seconds // 3600
    m = (total_seconds % 3600) // 60
    s = total_seconds % 60
    return f"{h:02d}:{m:02d}:{s:02d}"

def get_total_audio_seconds(audio_dir: Path) -> float:
    """Summer varighed af chapter_*.mp3 i sekunder."""
    try:
        from mutagen.mp3 import MP3
    except Exception:
        return 0.0
    total = 0.0
    for mp3 in sorted(audio_dir.glob("chapter_*.mp3")):
        try:
            total += float(MP3(str(mp3)).info.length)
        except Exception:
            pass
    return total

def _load_counter(db_path: Path) -> dict:
    if db_path.exists():
        try:
            return json.loads(db_path.read_text(encoding="utf-8"))
        except Exception:
            return {}
    return {}

def _save_counter(db_path: Path, data: dict) -> None:
    db_path.parent.mkdir(parents=True, exist_ok=True)
    db_path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")

def next_volume_label(prefix: str, db_path: Path) -> str:
    """DBS_DDMMYY_00001 (løbenr pr. dato)."""
    prefix = (prefix or "DBS").strip()
    date_key = datetime.datetime.now().strftime("%d%m%y")  # ddmmyy
    data = _load_counter(db_path)
    last = int(data.get(date_key, 0) or 0)
    new = last + 1
    data[date_key] = new
    _save_counter(db_path, data)
    return f"{prefix}_{date_key}_{new:05d}"

def _resolve_cmd(cmd: str, script_dir: Path) -> str:
(cmd: str, script_dir: Path) -> str:
    """Hvis cmd peger på en fil i script-mappen, brug fuld sti. Ellers returnér cmd uændret."""
    cmd = (cmd or "").strip()
    if not cmd:
        return cmd
    p = Path(cmd)
    if p.is_absolute():
        return cmd
    candidate = (script_dir / cmd)
    if candidate.exists():
        return str(candidate.resolve())
    return cmd

def run_pipeline(pipeline_cmd: str, args: list[str]):
    ensure_tool_exists(pipeline_cmd)
    subprocess.run([pipeline_cmd] + args, check=True)

def find_dtbook_xml(dtbook_out: Path) -> Path:
    hits = list(dtbook_out.rglob("dtbook.xml"))
    if hits:
        return hits[0]
    xmls = list(dtbook_out.rglob("*.xml"))
    if not xmls:
        raise FileNotFoundError(f"Kunne ikke finde dtbook.xml i {dtbook_out}")
    return xmls[0]

def make_pef_from_docx(docx_path: Path, pef_path: Path, pipeline_cmd: str, braille_table: str, work: Path):
    """DOCX -> DTBook -> PEF via DAISY Pipeline 2."""
    dtbook_out = work / "dtbook_out"
    pef_out = work / "pef_out"
    dtbook_out.mkdir(parents=True, exist_ok=True)
    pef_out.mkdir(parents=True, exist_ok=True)

    run_pipeline(pipeline_cmd, ["word-to-dtbook", "--source", str(docx_path), "-o", str(dtbook_out)])
    dtbook_xml = find_dtbook_xml(dtbook_out)

    run_pipeline(pipeline_cmd, [
        "dtbook-to-pef",
        "--source", str(dtbook_xml),
        "--braille-code", f"(liblouis-table:{braille_table})",
        "-o", str(pef_out)
    ])

    pefs = list(pef_out.rglob("*.pef"))
    if not pefs:
        raise FileNotFoundError("Pipeline lavede ingen .pef")
    pef_path.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(pefs[0], pef_path)


def _txt_to_temp_html(txt_path: Path, html_path: Path, lang: str = ""):
    """Lav en meget simpel HTML ud fra en .txt (blanke linjer => afsnit)."""
    text = txt_path.read_text(encoding="utf-8", errors="ignore")
    parts = [p.strip() for p in re.split(r"\n\s*\n+", text) if p.strip()]
    html_path.parent.mkdir(parents=True, exist_ok=True)
    with html_path.open("w", encoding="utf-8") as f:
        f.write("<!DOCTYPE html>\n")
        f.write("<html")
        if lang:
            f.write(f' lang="{html.escape(lang)}"')
        f.write("><head><meta charset=\"utf-8\"/></head><body>\n")
        for p in parts:
            f.write(f"<p>{html.escape(p)}</p>\n")
        f.write("</body></html>\n")

def make_pef_from_txt(txt_path: Path, pef_path: Path, pipeline_cmd: str, braille_table: str, work: Path, lang: str = ""):
    """TXT -> (temp HTML) -> PEF via DAISY Pipeline 2 (html-to-pef)."""
    html_in = work / "input.html"
    _txt_to_temp_html(txt_path, html_in, lang=lang)

    pef_out = work / "pef_out"
    pef_out.mkdir(parents=True, exist_ok=True)

    run_pipeline(pipeline_cmd, [
        "html-to-pef",
        "--source", str(html_in),
        "--braille-code", f"(liblouis-table:{braille_table})",
        "-o", str(pef_out)
    ])

    pefs = list(pef_out.rglob("*.pef"))
    if not pefs:
        raise FileNotFoundError("Pipeline lavede ingen .pef (html-to-pef)")
    pef_path.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(pefs[0], pef_path)

def _tts_cache_paths(cache_root: Path, voice_id: str, model_id: str, text: str):
    import hashlib, json as _json
    payload = _json.dumps(
        {"voice_id": voice_id, "model_id": model_id, "text": (text or "").strip()},
        ensure_ascii=False,
        separators=(",", ":"),
    ).encode("utf-8")
    h = hashlib.sha256(payload).hexdigest()
    # per-voice+model folder keeps cache tidy
    folder = cache_root / voice_id / (model_id or "default")
    return folder, h

def _elevenlabs_tts_raw(api_key: str, voice_id: str, model_id: str, text: str) -> bytes:
    resp = requests.post(
        f"https://api.elevenlabs.io/v1/text-to-speech/{voice_id}",
        headers={
            "xi-api-key": api_key,
            "Content-Type": "application/json",
        },
        json={
            "text": (text or "")[:5000],
            "model_id": model_id,
        },
        timeout=120
    )
    resp.raise_for_status()
    return resp.content

def elevenlabs_tts(api_key: str, voice_id: str, model_id: str, text: str,
                  cache_dir: Path | None = None, use_cache: bool = True) -> tuple[bytes, bool, Path | None]:
    """Text-to-speech med cache.

    Returnerer (audio_bytes, cache_hit, cache_path).
    - Hvis use_cache=True og der findes en cached mp3 for (voice_id, model_id, text), så bruges den og der kaldes ikke API.
    - Hvis ikke: kaldes ElevenLabs og resultatet gemmes i cache (hvis cache_dir er sat).
    """
    cache_path = None
    if cache_dir is not None:
        folder, h = _tts_cache_paths(cache_dir, voice_id, model_id, text)
        folder.mkdir(parents=True, exist_ok=True)
        cache_path = folder / f"{h}.mp3"
        meta_path = folder / f"{h}.json"

        if use_cache and cache_path.exists() and cache_path.stat().st_size > 0:
            return cache_path.read_bytes(), True, cache_path

    # ikke cache-hit -> kald API
    audio = _elevenlabs_tts_raw(api_key, voice_id, model_id, text)

    # gem i cache (best effort)
    if cache_dir is not None and cache_path is not None:
        try:
            tmp = cache_path.with_suffix(".tmp")
            tmp.write_bytes(audio)
            tmp.replace(cache_path)
            meta = {
                "voice_id": voice_id,
                "model_id": model_id,
                "chars": len((text or "")),
            }
            meta_path.write_text(json.dumps(meta, ensure_ascii=False, indent=2), encoding="utf-8")
        except Exception as e:
            print(f"ADVARSEL: Kunne ikke skrive TTS-cache: {e}")

    return audio, False, cache_path

def build_simple_daisy(book_name: str,
                       audio_dir: Path,
                       daisy_dir: Path,
                       text_chunks: list[str],
                       lang: str = "",
                       meta: dict | None = None):
    """Byg en simpel DAISY 2.02-lignende mappe (NCC + SMIL + MP3).

    - ncc.html med TOC + hele teksten opdelt i <p>
    - én SMIL pr. afsnit, der peger på ncc.html#p###
    - master.smil med centrale <meta> felter
    - kopierer MP3 ind i daisy_dir
    """
    meta = meta or {}
    daisy_dir.mkdir(parents=True, exist_ok=True)

    mp3_files = sorted(audio_dir.glob("chapter_*.mp3"))
    if not mp3_files:
        raise FileNotFoundError(f"Ingen chapter_*.mp3 fundet i {audio_dir}")

    n = min(len(mp3_files), len(text_chunks))
    if len(mp3_files) != len(text_chunks):
        print(f"ADVARSEL: mp3={len(mp3_files)} og tekst={len(text_chunks)} matcher ikke. Bruger n={n}.")

    total_secs = get_total_audio_seconds(audio_dir)
    total_hhmmss = _format_hhmmss(total_secs)

    # metadata (med fallback)
    dc_title = (meta.get("Titel") or book_name).strip()
    dc_format = (meta.get("Daisy vertion") or "DAISY 2.02").strip()
    dc_identifier = (meta.get("Volume label") or "").strip()
    generator = f"AI Daisy {__version__}"

    # master.smil (kun metadata)
    master_path = daisy_dir / "master.smil"
    with master_path.open("w", encoding="utf-8") as f:
        f.write('<?xml version="1.0" encoding="utf-8"?>
')
        f.write("<smil>
  <head>
")
        f.write(f'    <meta name="dc:title" content="{html.escape(dc_title)}" />
')
        f.write(f'    <meta name="dc:format" content="{html.escape(dc_format)}" />
')
        if dc_identifier:
            f.write(f'    <meta name="dc:identifier" content="{html.escape(dc_identifier)}" />
')
        if total_hhmmss:
            f.write(f'    <meta name="ncc:totalElapsedTime" content="{html.escape(total_hhmmss)}" />
')
            f.write(f'    <meta name="ncc:timeInThisSmil" content="{html.escape(total_hhmmss)}" />
')
        f.write(f'    <meta name="ncc:generator" content="{html.escape(generator)}" />
')
        f.write("  </head>
  <body/>
</smil>
")

    # ncc.html
    ncc_path = daisy_dir / "ncc.html"
    with ncc_path.open("w", encoding="utf-8") as ncc:
        ncc.write("<!DOCTYPE html>
")
        ncc.write("<html")
        if lang:
            ncc.write(f' lang="{html.escape(lang)}"')
        ncc.write(">
<head>
")
        ncc.write('  <meta charset="utf-8"/>
')
        ncc.write(f"  <title>{html.escape(book_name)}</title>
")

        # centrale meta felter (samme som i master.smil)
        ncc.write(f'  <meta name="dc:title" content="{html.escape(dc_title)}" />
')
        ncc.write(f'  <meta name="dc:format" content="{html.escape(dc_format)}" />
')
        if lang:
            ncc.write(f'  <meta name="dc:language" content="{html.escape(lang)}" />
')
        if dc_identifier:
            ncc.write(f'  <meta name="dc:identifier" content="{html.escape(dc_identifier)}" />
')
        if total_hhmmss:
            ncc.write(f'  <meta name="ncc:totalElapsedTime" content="{html.escape(total_hhmmss)}" />
')
        ncc.write(f'  <meta name="ncc:generator" content="{html.escape(generator)}" />
')

        ncc.write("</head>
<body>
")
        ncc.write(f"  <h1>{html.escape(book_name)}</h1>
")

        # TOC
        ncc.write("  <h2>Indhold</h2>
")
        for i in range(1, n + 1):
            ncc.write(f'  <div><a href="chapter_{i:03}.smil#par{i:03}">Afsnit {i}</a></div>
')

        ncc.write("  <hr/>
")
        ncc.write("  <h2>Tekst</h2>
")
        for i in range(1, n + 1):
            txt = (text_chunks[i - 1] or "").strip()
            ncc.write(
                f'  <p id="p{i:03}"><a href="chapter_{i:03}.smil#par{i:03}">{html.escape(txt)}</a></p>
'
            )
        ncc.write("</body>
</html>
")

    # kopiér MP3 + lav SMIL
    for i in range(1, n + 1):
        mp3_path = mp3_files[i - 1]
        shutil.copy2(mp3_path, daisy_dir / mp3_path.name)

        smil_path = daisy_dir / f"chapter_{i:03}.smil"
        with smil_path.open("w", encoding="utf-8") as smil:
            smil.write('<?xml version="1.0" encoding="utf-8"?>
')
            smil.write("<smil>
  <body>
    <seq>
")
            smil.write(f'      <par id="par{i:03}">
')
            smil.write(f'        <text src="ncc.html#p{i:03}"/>
')
            smil.write(f'        <audio src="{html.escape(mp3_path.name)}" clip-begin="0s"/>
')
            smil.write("      </par>
")
            smil.write("    </seq>
  </body>
</smil>
")

def create_iso_via_powershell_imapi(source_dir: Path, iso_path: Path, volume_name: str):
    '''Create an ISO from a folder using Windows' built-in IMAPI2 (via PowerShell).

    This avoids needing DAISY Pipeline for ISO creation.
    '''
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

    ps_template = r'''$ErrorActionPreference='Stop'
$src = '__SRC__'
$dst = '__DST__'
$vol = '__VOL__'

if (-not (Test-Path -LiteralPath $src)) { throw "Kilde-mappen findes ikke: $src" }
$parent = Split-Path -Parent $dst
if ($parent -and -not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent | Out-Null }

# Compile a small .NET helper once to reliably copy IMAPI2's COM IStream to a file.
if (-not ('IsoWriter' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

public static class IsoWriter
{
    public static void SaveStreamToFile(object comStream, string path)
    {
        IntPtr unk = Marshal.GetIUnknownForObject(comStream);
        try
        {
            var istream = (IStream)Marshal.GetTypedObjectForIUnknown(unk, typeof(IStream));
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[32768];
                IntPtr pcbRead = Marshal.AllocHGlobal(sizeof(int));
                try
                {
                    while (true)
                    {
                        istream.Read(buffer, buffer.Length, pcbRead);
                        int read = Marshal.ReadInt32(pcbRead);
                        if (read <= 0) break;
                        fs.Write(buffer, 0, read);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pcbRead);
                }
            }
        }
        finally
        {
            if (unk != IntPtr.Zero) Marshal.Release(unk);
        }
    }
}
'@ | Out-Null
}

$fs = New-Object -ComObject IMAPI2FS.MsftFileSystemImage
# 1=ISO9660, 2=Joliet, 4=UDF => 7 = ISO9660+Joliet+UDF (bedst til lange filnavne)
$fs.FileSystemsToCreate = 7
$fs.VolumeName = $vol
# UDF 1.02
try { $fs.UDFRevision = 0x102 } catch { }

$fs.Root.AddTree($src, $false) | Out-Null
$result = $fs.CreateResultImage()
$img = $result.ImageStream

[IsoWriter]::SaveStreamToFile($img, $dst)
'''

    ps = ps_template.replace("__SRC__", esc_ps(src)).replace("__DST__", esc_ps(dst)).replace("__VOL__", esc_ps(vol))

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

def process_one_file(input_file: Path,
                     api_key: str,
                     voice_id: str,
                     voice_name: str,
                     model_id: str,
                     iso_cmd: str,
                     mode: str,
                     lang: str,
                     volume_label: str,
                     meta_base: dict,
                     max_tts_chars: int = 4800,
                     cache_dir: Path | None = None,
                     use_tts_cache: bool = True,
                     settings: dict | None = None):
    """Processér én fil og lav ISO/CSV/PEF afhængigt af mode.

    mode: "daisy" | "pef" | "both"
    """
    settings = settings or {}
    input_file = input_file.resolve()
    book_name = input_file.stem

    make_daisy = mode in ("daisy", "both")
    make_pef = mode in ("pef", "both")

    # output stier (i samme mappe som input)
    output_csv = input_file.with_suffix(".csv")
    output_iso = input_file.with_suffix(".iso")
    output_pef = input_file.with_suffix(".pef")

    if make_daisy:
        output_iso = _choose_writable_output_path(output_iso)

    # metadata (start med base og udfyld efterhånden)
    meta = dict(meta_base or {})
    meta["Stemme"] = voice_name if make_daisy else meta.get("Stemme", "")
    meta["Sprog"] = (lang or "").lower()
    meta["Volume label"] = volume_label
    meta["orginaldokomenter navne+format"] = meta.get("orginaldokomenter navne+format") or input_file.name

    # midlertidig arbejdsmappe
    work = Path(tempfile.mkdtemp(prefix="daisy_work_"))
    text_file = work / "text.txt"
    audio_dir = work / "audio"
    audio_dir.mkdir(parents=True, exist_ok=True)

    daisy_dir = work / book_name

    try:
        paragraphs = []

        if make_daisy:
            if not api_key:
                raise RuntimeError("Mangler ELEVEN_API_KEY i secrets.json (kræves for DAISY/TTS).")

            # DOCX/TXT -> tekst
            docx_to_text(input_file, text_file)

            # læs + split
            full_text = text_file.read_text(encoding="utf-8", errors="ignore")
            paragraphs = split_text_into_paragraphs(full_text, max_chars=max_tts_chars)
            if not paragraphs:
                raise ValueError("Ingen tekst fundet i dokumentet.")

            # TTS pr. afsnit
            for i, para in enumerate(paragraphs, start=1):
                print(f"[{input_file.name}] Laver lyd {i}/{len(paragraphs)} ...")
                mp3_bytes, cache_hit, cache_path = elevenlabs_tts(
                    api_key, voice_id, model_id, para,
                    cache_dir=cache_dir, use_cache=use_tts_cache
                )
                if cache_hit:
                    print(f"    -> cache hit: {cache_path}")
                (audio_dir / f"chapter_{i:03}.mp3").write_bytes(mp3_bytes)

            # længde (hh:mm:ss)
            total_secs = get_total_audio_seconds(audio_dir)
            meta["Lengte"] = _format_hhmmss(total_secs)

            # (valgfrit) segment-csv
            if settings.get("WRITE_SEGMENT_CSV"):
                seg_csv = input_file.with_name(f"{input_file.stem}_segments.csv")
                write_segments_csv(paragraphs, seg_csv)
                print(f"[{input_file.name}] Segments-CSV: {seg_csv}")

            # DAISY-filer (inkl. master.smil + ncc.html)
            meta["Daisy vertion"] = meta.get("Daisy vertion") or "DAISY 2.02"
            build_simple_daisy(book_name, audio_dir, daisy_dir, paragraphs, lang=lang, meta=meta)

            # sanity: ncc.html skal findes
            ncc = daisy_dir / "ncc.html"
            if not ncc.exists():
                raise FileNotFoundError(f"ncc.html mangler: {ncc}")

            # ISO (volume label på selve ISO'en)
            meta["Write data"] = output_iso.name
            print(f"[{input_file.name}] Laver ISO ...")
            create_iso(iso_cmd, daisy_dir, output_iso, volume_name=volume_label)
            print(f"FÆRDIG ISO: {output_iso}")
        else:
            # Hvis vi kun laver PEF/metadata: ingen lyd => længde tom
            meta["Lengte"] = meta.get("Lengte", "")
            meta["Daisy vertion"] = meta.get("Daisy vertion", "")

        if make_pef:
            # PEF (braille) via DAISY Pipeline 2
            script_dir = Path(__file__).resolve().parent
            pipeline_cmd = _resolve_cmd((settings.get("DAISY_PIPELINE_CMD") or "pipeline2"), script_dir)
            tables = settings.get("BRAILLE_TABLE_BY_LANG") or {}
            braille_table = (tables.get((lang or "").lower()) or "").strip()

            if not braille_table:
                print(f"[{input_file.name}] PEF springes over: ingen BRAILLE_TABLE_BY_LANG for '{lang}'.")
            else:
                print(f"[{input_file.name}] Laver PEF ({lang} -> {braille_table}) ...")
                if input_file.suffix.lower() == ".docx":
                    make_pef_from_docx(input_file, output_pef, pipeline_cmd, braille_table, work)
                elif input_file.suffix.lower() == ".txt":
                    make_pef_from_txt(input_file, output_pef, pipeline_cmd, braille_table, work, lang=lang)
                else:
                    print(f"[{input_file.name}] PEF springes over: ukendt filtype {input_file.suffix}")
                if output_pef.exists():
                    print(f"FÆRDIG PEF: {output_pef}")

        # Metadata CSV (altid)
        write_metadata_csv(meta, output_csv)
        print(f"[{input_file.name}] Metadata-CSV: {output_csv}")

    finally:
        shutil.rmtree(work, ignore_errors=True)

def main():
    script_dir = Path(__file__).resolve().parent
    secrets_path = script_dir / "secrets.json"
    settings_path = script_dir / "settings.json"
    voices_path = script_dir / "voices.json"

    # settings.json: stier og øvrige indstillinger (valgfri fil)
    settings = load_optional_json(settings_path, default={})

    # ----- Vælg hvad der skal produceres -----
    mode = choose_from_list(
        "Hvad vil du lave?",
        ["both", "daisy", "pef"],
        lambda m: {
            "both": "Begge dele (DAISY/ISO + Punkt/PEF)",
            "daisy": "Kun DAISY/ISO (lyd + ncc.html)",
            "pef": "Kun Punkt/PEF"
        }.get(m, m)
    )
    make_daisy = mode in ("both", "daisy")
    make_pef = mode in ("both", "pef")

    # ----- Secrets/API-key (kun nødvendig for DAISY/TTS) -----
    secrets = load_or_create_secrets(secrets_path)
    api_key = (secrets.get("ELEVEN_API_KEY") or "").strip()

    # ----- (Valgfrit) opdater voices.json via PowerShell -----
    raw_voices = load_optional_json(voices_path, default=None)
    voices = []
    if make_daisy:
        if voices_path.exists():
            ps1 = script_dir / "Export-ElevenLabsVoices_v12_1.ps1"
            if ps1.exists():
                ans = input("Opdatere voices.json nu? (j/N): ").strip().lower()
                if ans in ("j", "ja", "y", "yes"):
                    ok = run_powershell_export_voices(script_dir, secrets_path)
                    if ok:
                        raw_voices = load_optional_json(voices_path, default=None)

        # Sørg for at vi har en liste af stemmer med voice_id
        voices = ensure_voices_list(raw_voices, api_key, voices_path)

    # ----- Vælg sprog -----
    def choose_lang_any():
        # foreslå sprog fra BRAILLE_TABLE_BY_LANG + voices.json
        keys = set()
        try:
            keys.update((settings.get("BRAILLE_TABLE_BY_LANG") or {}).keys())
        except Exception:
            pass
        for v in (voices or []):
            for l in _voice_langs(v):
                keys.add(l)
        keys = sorted(k.lower() for k in keys if k)
        # altid tilbyd da/en først hvis de findes
        preferred = [k for k in ["da", "en"] if k in keys]
        rest = [k for k in keys if k not in preferred]
        items = preferred + rest

        if make_daisy and items:
            items = ["auto"] + items

        items.append("andet")

        choice = choose_from_list(
            "Vælg sprog (fx da/en) – bogen er 1-sprogs:",
            items,
            lambda c: f"{c} ({LANG_NAMES.get(c, c)})" if c in LANG_NAMES else c
        )
        if choice == "andet":
            return input("Indtast sprogkode (fx da, en): ").strip().lower()
        return choice

    lang = choose_lang_any()

    # ----- Vælg stemme (kun hvis DAISY) -----
    voice_id = ""
    voice_name = ""
    if make_daisy:
        voices_for_lang = voices
        if lang not in ("", "auto"):
            voices_for_lang = [v for v in voices if lang in _voice_langs(v)]
            if not voices_for_lang:
                print("Ingen stemmer matcher sproget – viser alle stemmer.")
                voices_for_lang = voices

        selected_voice = choose_from_list(
            "Vælg stemme:",
            voices_for_lang,
            lambda v: f"{v.get('name','(uden navn)')} (voice_id: {_get_voice_id(v)})"
        )
        voice_id = (_get_voice_id(selected_voice) or "").strip()
        voice_name = (selected_voice.get("name") or "").strip()

        if lang == "auto":
            vl = _voice_langs(selected_voice)
            lang = vl[0] if vl else ""

    # ----- Filvalg -----
    # Standard root fra settings hvis angivet
    default_root = settings.get("DEFAULT_ROOT") or DEFAULT_ROOT
    root_dir = Path(default_root).expanduser()
    root_dir.mkdir(parents=True, exist_ok=True)

    folders = [p for p in root_dir.iterdir() if p.is_dir()]
    folders = sorted(folders, key=lambda p: p.name.lower())
    if not folders:
        print(f"Ingen mapper fundet i: {root_dir}")
        sys.exit(1)

    selected_folder = choose_from_list(
        f"Vælg mappe i {root_dir}:",
        folders,
        lambda p: p.name
    )

    files = []
    for ext in ("*.docx", "*.txt"):
        files.extend(selected_folder.glob(ext))
    files = sorted(files, key=lambda p: p.name.lower())
    if not files:
        print(f"Ingen .docx eller .txt fundet i: {selected_folder}")
        sys.exit(1)

    selected_files = choose_file_from_list(
        f"Vælg fil i {selected_folder}:",
        files
    )

    # ----- Produktionsløbenr / volume label -----
    counter_path = settings.get("COUNTER_DB_PATH") or "production_counter.json"
    counter_path = Path(counter_path)
    if not counter_path.is_absolute():
        counter_path = script_dir / counter_path

    volume_prefix = settings.get("VOLUME_PREFIX") or "DBS"

    # ----- Metadata valg (lister + indtast) -----
    produced_for_opts = settings.get("PRODUCED_FOR_OPTIONS") or ["Solgavevalby", "Dansk blindesamfund"]
    produced_by_opts = settings.get("PRODUCED_BY_OPTIONS") or ["Dansk Blindesamfund"]
    sender_opts = settings.get("SENDER_OPTIONS") or [
        "Dansk Blindesamfund | Blekinge Boulevard 2 | 2630 Taastrup"
    ]

    def choose_or_input(title: str, options: list[str]) -> str:
        items = list(options) + ["(Andet – indtast)", "(Tom)"]
        c = choose_from_list(title, items, lambda x: x)
        if c.startswith("(Andet"):
            return input("Indtast værdi: ").strip()
        if c.startswith("(Tom"):
            return ""
        return c

    # Model/ISO/TTS-cache settings
    model_id = (settings.get("MODEL_ID") or "eleven_multilingual_v2").strip()
    iso_cmd = (settings.get("ISO_CMD") or "powershell-imapi2").strip()
    max_tts_chars = int(settings.get("MAX_TTS_CHARS") or 4800)
    use_tts_cache = bool(settings.get("USE_TTS_CACHE", True))
    cache_dir = (script_dir / (settings.get("CACHE_DIR") or "tts_cache")).resolve()

    # kør pr. fil
    for f in selected_files:
        volume_label = next_volume_label(volume_prefix, counter_path)

        # metadata base (udfyldes færdigt i process_one_file)
        now = datetime.datetime.now()
        meta_base = {
            "Pruduceret for": choose_or_input("Pruduceret for:", produced_for_opts),
            "Titel": input(f"Titel (Enter = {f.stem}): ").strip() or f.stem,
            "Dato for Intale": now.strftime("%d-%m-%Y"),
            "Tid for intale": now.strftime("%H:%M:%S"),
            "Pruduceret af": choose_or_input("Pruduceret af:", produced_by_opts),
            "Afsender": choose_or_input("Afsender:", sender_opts),
            # resten sættes automatisk
        }

        process_one_file(
            f,
            api_key=api_key,
            voice_id=voice_id,
            voice_name=voice_name,
            model_id=model_id,
            iso_cmd=iso_cmd,
            mode=mode,
            lang=lang,
            volume_label=volume_label,
            meta_base=meta_base,
            max_tts_chars=max_tts_chars,
            cache_dir=cache_dir,
            use_tts_cache=use_tts_cache,
            settings=settings
        )

if __name__ == "__main__":
    script_dir = Path(__file__).resolve().parent
    log_path, _log_fh = _setup_logfile(script_dir)
    # Tee stdout/stderr til logfil
    _old_out, _old_err = sys.stdout, sys.stderr
    sys.stdout = _TeeTextIO(_old_out, _log_fh)
    sys.stderr = _TeeTextIO(_old_err, _log_fh)

    print(f"Logfil: {log_path}")

    try:
        main()
    except SystemExit:
        # behold exit-koder, men vis stadig en pause så konsolvindue ikke lukker med det samme
        raise
    except Exception as e:
        print("\nFEJL (uventet):", e)
        import traceback
        traceback.print_exc()
        print(f"\nDetaljer er gemt i logfilen: {log_path}")
    finally:
        try:
            input("\nTryk Enter for at afslutte...")
        except Exception:
            pass
        try:
            _log_fh.flush()
            _log_fh.close()
        except Exception:
            pass
        # gendan streams (pænt hvis kørt fra IDLE)
        try:
            sys.stdout = _old_out
            sys.stderr = _old_err
        except Exception:
            pass
