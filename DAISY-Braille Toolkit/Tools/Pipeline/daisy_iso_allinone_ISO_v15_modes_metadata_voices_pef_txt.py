#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import os, sys, subprocess, tempfile, shutil, json, csv, html, textwrap, hashlib
import datetime
from pathlib import Path

try:
    import requests
except Exception as e:
    print("FEJL: 'requests' mangler. Installer med: pip install requests")
    raise

__version__ = "v15-modes-metadata-voices-pef-txt"

# ===== Defaults =====
DEFAULT_ROOT = r"C:\DAISY-BOOKS\originaldokumenter"
DEFAULT_MODEL_ID = "eleven_multilingual_v2"
DEFAULT_MAX_TTS_CHARS = 4800

# ===== JSON helpers =====
def load_json(path: Path) -> dict:
    with path.open("r", encoding="utf-8-sig") as f:
        return json.load(f)

def load_optional_json(path: Path, default):
    try:
        if path.exists():
            return load_json(path)
    except Exception:
        return default
    return default

def save_json(path: Path, obj):
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False, indent=2)

def load_or_create_secrets(secrets_path: Path) -> dict:
    if secrets_path.exists():
        return load_json(secrets_path)

    print(f"FEJL: Mangler fil: {secrets_path}")
    print("Jeg kan oprette den for dig nu.")
    api_key = ""
    try:
        import getpass
        api_key = (getpass.getpass("Indsæt ELEVEN_API_KEY (skjult input): ") or "").strip()
    except Exception:
        api_key = (input("Indsæt ELEVEN_API_KEY: ") or "").strip()

    if not api_key:
        raise RuntimeError("Ingen ELEVEN_API_KEY angivet.")

    save_json(secrets_path, {"ELEVEN_API_KEY": api_key})
    print("OK: secrets.json oprettet.")
    return {"ELEVEN_API_KEY": api_key}

# ===== UI helpers =====
def choose_from_list(title: str, items, render=lambda x: str(x)):
    print("\n" + title)
    for i, it in enumerate(items):
        print(f"[{i}] {render(it)}")
    while True:
        raw = (input(f"Vælg nummer (0-{len(items)-1}): ") or "").strip()
        if raw.isdigit():
            idx = int(raw)
            if 0 <= idx < len(items):
                return items[idx]
        print("Ugyldigt valg. Prøv igen.")

def list_subfolders(root: Path):
    return sorted([p for p in root.iterdir() if p.is_dir()], key=lambda p: p.name.lower())

def list_files(folder: Path):
    return sorted([p for p in folder.iterdir() if p.is_file() and p.suffix.lower() in (".txt", ".docx")],
                  key=lambda p: p.name.lower())

# ===== Language / mode =====
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

def choose_output_mode():
    modes = [
        ("daisy", "Kun DAISY/ISO (+ metadata CSV)"),
        ("braille", "Kun Punkt (PEF) (+ metadata CSV)"),
        ("both", "Begge dele: DAISY/ISO + PEF (+ metadata CSV)")
    ]
    picked = choose_from_list("Hvad vil du lave?", modes, lambda x: x[1])
    return picked[0]

# ===== voices.json update via PowerShell (optional) =====
def maybe_update_voices_via_powershell(script_dir: Path, secrets_path: Path, voices_path: Path):
    ps = script_dir / "Export-ElevenLabsVoices_v12_1.ps1"
    if not ps.exists():
        return

    if voices_path.exists():
        ans = (input("Opdatér voices.json nu? (j/N): ").strip().lower() or "n")
        if ans != "j":
            return
    else:
        ans = (input("voices.json mangler. Vil du generere den nu? (j/N): ").strip().lower() or "n")
        if ans != "j":
            return

    try:
        subprocess.run([
            "powershell", "-NoProfile", "-ExecutionPolicy", "Bypass",
            "-File", str(ps),
            "-SecretsPath", str(secrets_path),
            "-RebuildVoicesJson"
        ], check=True)
        print("voices.json opdateret.")
    except Exception as e:
        print("ADVARSEL: Kunne ikke opdatere voices.json via PowerShell.")
        print(f"Fejl: {e}")

def normalize_voices(raw):
    # raw kan være list eller dict med "voices"
    if raw is None:
        return []
    if isinstance(raw, list):
        return raw
    if isinstance(raw, dict):
        if "voices" in raw and isinstance(raw["voices"], list):
            return raw["voices"]
        # hvis PS-scriptet skriver liste direkte
    return []

def get_voice_id(v):
    return (v.get("voice_id") or v.get("voiceId") or v.get("id") or "").strip()

def fetch_voices_from_api(api_key: str):
    # fallback hvis voices.json mangler
    url = "https://api.elevenlabs.io/v1/voices"
    r = requests.get(url, headers={"xi-api-key": api_key}, timeout=60)
    r.raise_for_status()
    data = r.json()
    vs = data.get("voices") or []
    out = []
    for v in vs:
        out.append({
            "name": v.get("name",""),
            "voice_id": v.get("voice_id",""),
            "languages": ",".join([x.get("language","") for x in (v.get("labels") or [])]) if isinstance(v.get("labels"), list) else ""
        })
    return out

# ===== Metadata template + CSV =====
def load_metadata_template(meta_path: Path) -> dict:
    if not meta_path.exists():
        return {"fields": [], "options": {}, "produced_for": []}

    raw = meta_path.read_text(encoding="utf-8-sig", errors="ignore")
    lines = [l.strip() for l in raw.splitlines()]

    # fields
    fields = []
    try:
        start = lines.index("Data der skal i CSV filen") + 1
    except ValueError:
        start = 0

    i = start
    while i < len(lines):
        if lines[i].startswith("..."):
            i += 1
            break
        if lines[i]:
            fields.append(lines[i])
        i += 1

    # produced_for options (linjer efter "..." indtil næste "* liste")
    produced_for = []
    j = i
    while j < len(lines):
        if lines[j].lower().endswith("liste"):
            break
        if lines[j]:
            produced_for.append(lines[j])
        j += 1

    # parse "* liste" sections
    options = {}
    k = j
    while k < len(lines):
        if not lines[k]:
            k += 1
            continue
        if lines[k].lower().endswith("liste"):
            section = lines[k]
            k += 1
            vals = []
            while k < len(lines) and lines[k] and not lines[k].lower().endswith("liste"):
                vals.append(lines[k])
                k += 1
            if vals:
                options[section] = vals
        else:
            k += 1

    return {"fields": fields, "options": options, "produced_for": produced_for}

def write_metadata_csv(headers: list[str], row: list[str], csv_path: Path):
    csv_path.parent.mkdir(parents=True, exist_ok=True)
    with csv_path.open("w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f, delimiter=";")
        w.writerow(headers)
        w.writerow(row)

def _join_address(lines_list: list[str]) -> str:
    return ", ".join([x.strip() for x in lines_list if x.strip()])

def prompt_metadata(template: dict, *, title_default: str, voice_name: str, lang: str,
                    original_names: list[str], iso_name: str, volume_label: str, length_str: str):
    fields = template.get("fields") or []
    options = template.get("options") or {}
    produced_for_opts = template.get("produced_for") or []

    def pick_or_input(label: str, opts: list[str], default: str = "") -> str:
        if opts:
            items = opts + ["(indtast selv)"]
            pick = choose_from_list(label, items, lambda x: x)
            if pick == "(indtast selv)":
                return (input(f"{label} (indtast): ").strip() or default)
            return pick
        return (input(f"{label} (indtast): ").strip() or default)

    produced_for = pick_or_input("Pruduceret for", produced_for_opts, default="")
    title = input(f"Titel (Enter = {title_default}): ").strip() or title_default

    prod_by_opts = options.get("Pruduceret af liste", [])
    produced_by = pick_or_input("Pruduceret af", prod_by_opts, default=(prod_by_opts[0] if prod_by_opts else ""))

    sender_lines = options.get("Afsender liste", [])
    sender_default = _join_address(sender_lines) if sender_lines else ""
    sender = input(f"Afsender (Enter = {sender_default}): ").strip() or sender_default

    now = datetime.datetime.now()
    date_str = now.strftime("%d%m%y")
    time_str = now.strftime("%H:%M:%S")

    # map values by (partial) field names from template
    values = {}
    for f in fields:
        fl = f.lower()
        if fl.startswith("lengte"):
            values[f] = length_str
        elif fl.startswith("stemme"):
            values[f] = voice_name
        elif fl.startswith("sprog"):
            values[f] = lang
        elif "pruduceret for" in fl:
            values[f] = produced_for
        elif fl.startswith("titel"):
            values[f] = title
        elif "dato" in fl and "intale" in fl:
            values[f] = date_str
        elif "tid" in fl and "intale" in fl:
            values[f] = time_str
        elif "pruduceret af" in fl:
            values[f] = produced_by
        elif "daisy vertion" in fl:
            values[f] = "DAISY (simpel ncc.html + smil)"
        elif "orginal" in fl and "dokomenter" in fl:
            values[f] = ";".join(original_names)
        elif fl.startswith("afsender"):
            values[f] = sender
        elif fl.startswith("volume label"):
            values[f] = volume_label
        elif fl.startswith("write data"):
            values[f] = iso_name
        else:
            values[f] = ""

    if not fields:
        # fallback hvis template mangler
        fields = ["Lengte","Stemme","Sprog","Titel","Dato","Tid","Orginal","Volume label","Write data"]
        values = {k:"" for k in fields}

    return fields, [values.get(h,"") for h in fields]

# ===== Text conversion =====
def docx_to_text(input_file: Path, text_file: Path):
    suf = input_file.suffix.lower()
    if suf == ".txt":
        text_file.write_text(input_file.read_text(encoding="utf-8", errors="ignore"), encoding="utf-8")
        return
    if suf == ".docx":
        try:
            import docx  # python-docx
            d = docx.Document(str(input_file))
            paras = []
            for p in d.paragraphs:
                txt = (p.text or "").strip()
                if txt:
                    paras.append(txt)
            text_file.write_text("\n\n".join(paras), encoding="utf-8")
            return
        except Exception:
            # fallback pandoc
            pandoc = shutil.which("pandoc")
            if not pandoc:
                raise
            subprocess.run([pandoc, "-t", "plain", str(input_file), "-o", str(text_file)], check=True)

def split_text_into_chunks(text: str, max_chars: int):
    paras = [p.strip() for p in text.split("\n\n") if p.strip()]
    out = []
    for p in paras:
        if len(p) <= max_chars:
            out.append(p)
            continue
        # split long paragraph
        start = 0
        while start < len(p):
            out.append(p[start:start+max_chars])
            start += max_chars
    if not out:
        out = [text.strip()] if text.strip() else []
    return out

# ===== ElevenLabs TTS + cache =====
def sha256_hex(s: str) -> str:
    return hashlib.sha256(s.encode("utf-8")).hexdigest()

def tts_cache_path(cache_dir: Path, voice_id: str, model_id: str, text: str) -> Path:
    h = sha256_hex(f"{voice_id}|{model_id}|{text}")
    return cache_dir / voice_id / model_id / f"{h}.mp3"

def elevenlabs_tts_mp3(api_key: str, voice_id: str, model_id: str, text: str) -> bytes:
    url = f"https://api.elevenlabs.io/v1/text-to-speech/{voice_id}"
    headers = {
        "xi-api-key": api_key,
        "Content-Type": "application/json",
        "accept": "audio/mpeg",
    }
    payload = {
        "text": text,
        "model_id": model_id,
    }
    r = requests.post(url, headers=headers, json=payload, timeout=120)
    r.raise_for_status()
    return r.content

# ===== DAISY builder =====
def build_simple_daisy(book_name: str, audio_dir: Path, daisy_dir: Path, text_chunks: list[str], lang: str = ""):
    daisy_dir.mkdir(parents=True, exist_ok=True)
    mp3_files = sorted(audio_dir.glob("chapter_*.mp3"))
    if not mp3_files:
        raise FileNotFoundError(f"Ingen chapter_*.mp3 fundet i {audio_dir}")
    n = min(len(mp3_files), len(text_chunks))

    ncc_path = daisy_dir / "ncc.html"
    with ncc_path.open("w", encoding="utf-8") as ncc:
        ncc.write("<!DOCTYPE html>\n")
        ncc.write("<html" + (f' lang="{html.escape(lang)}"' if lang else "") + ">\n<head>\n")
        ncc.write('  <meta charset="utf-8"/>\n')
        ncc.write(f"  <title>{html.escape(book_name)}</title>\n")
        if lang:
            ncc.write(f'  <meta name="dc:language" content="{html.escape(lang)}"/>\n')
        ncc.write("</head>\n<body>\n")
        ncc.write(f"  <h1>{html.escape(book_name)}</h1>\n")
        ncc.write("  <h2>Indhold</h2>\n")
        for i in range(1, n+1):
            ncc.write(f'  <div><a href="chapter_{i:03}.smil#par{i:03}">Afsnit {i}</a></div>\n')
        ncc.write("  <hr/>\n")
        ncc.write("  <h2>Tekst</h2>\n")
        for i in range(1, n+1):
            txt = (text_chunks[i-1] or "").strip()
            ncc.write(f'  <p id="p{i:03}"><a href="chapter_{i:03}.smil#par{i:03}">{html.escape(txt)}</a></p>\n')
        ncc.write("</body>\n</html>\n")

    for i in range(1, n+1):
        mp3_path = mp3_files[i-1]
        shutil.copy2(mp3_path, daisy_dir / mp3_path.name)
        smil_path = daisy_dir / f"chapter_{i:03}.smil"
        with smil_path.open("w", encoding="utf-8") as smil:
            smil.write('<?xml version="1.0" encoding="utf-8"?>\n')
            smil.write("<smil>\n  <body>\n    <seq>\n")
            smil.write(f'      <par id="par{i:03}">\n')
            smil.write(f'        <text src="ncc.html#p{i:03}"/>\n')
            smil.write(f'        <audio src="{html.escape(mp3_path.name)}" clip-begin="0s"/>\n')
            smil.write("      </par>\n")
            smil.write("    </seq>\n  </body>\n</smil>\n")

# ===== ISO creation via IMAPI2 PowerShell =====
def _choose_writable_output_path(path: Path) -> Path:
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

def create_iso_via_powershell_imapi2(source_dir: Path, iso_path: Path, volume_name: str):
    src = str(source_dir.resolve())
    dst = str(iso_path.resolve())
    vol = (volume_name or "DAISY").strip()

    # volume label: A-Z0-9_
    import re as _re
    vol = _re.sub(r"[^A-Za-z0-9_]", "_", vol).upper() or "DAISY"
    if len(vol) > 32:
        vol = vol[:32]

    ps = textwrap.dedent(f"""
    $ErrorActionPreference = "Stop"
    $src = '{src}'
    $dst = '{dst}'
    $vol = '{vol}'

    $fsi = New-Object -ComObject IMAPI2FS.MsftFileSystemImage
    $fsi.FileSystemsToCreate = 1  # ISO9660
    $fsi.VolumeName = $vol
    $fsi.ChooseImageDefaultsForMediaType(12) | Out-Null  # 12 = IMAPI_MEDIA_PHYSICAL_TYPE_DISK
    $fsi.Root.AddTree($src, $false)

    $result = $fsi.CreateResultImage()
    $stream = $result.ImageStream

    $fileStream = New-Object -ComObject ADODB.Stream
    $fileStream.Type = 1
    $fileStream.Open()
    $fileStream.Write($stream.Read($stream.Size))
    $fileStream.SaveToFile($dst, 2)
    $fileStream.Close()
    """).strip()

    with tempfile.NamedTemporaryFile("w", suffix=".ps1", delete=False, encoding="utf-8") as tf:
        tf.write(ps)
        ps_path = tf.name

    try:
        subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ps_path], check=True)
    finally:
        try:
            os.remove(ps_path)
        except Exception:
            pass

def create_iso(iso_cmd: str, source_dir: Path, iso_path: Path, volume_name: str):
    # I denne pakke bruger vi PowerShell IMAPI2 som standard
    create_iso_via_powershell_imapi2(source_dir, iso_path, volume_name)

# ===== Volume label counter =====
def next_volume_label(script_dir: Path, settings: dict, *, max_seq: int = 999) -> str:
    """<PREFIX>_DDMMYY_NNN (løbenr pr. prefix+dato).

    - Prefix hentes fra settings['VOLUME_PREFIX'] (fallback: DBS)
    - COUNTER_DB_PATH kan angives i settings (fallback: production_counter.json i script-mappen)
    - NNN: 000..999 (nulstilles når den når 999)
    """
    counter_path = Path(settings.get('COUNTER_DB_PATH') or 'production_counter.json')
    if not counter_path.is_absolute():
        counter_path = script_dir / counter_path
    prefix = (settings.get('VOLUME_PREFIX') or 'DBS').strip()
    date_key = datetime.datetime.now().strftime('%d%m%y')
    data = load_optional_json(counter_path, default={})
    key = f"{prefix}_{date_key}"
    next_no = int(data.get(key, 0) or 0)
    if next_no > max_seq:
        raise RuntimeError(f"Sequence exhausted for {key}: {next_no} > {max_seq}")
    data[key] = next_no + 1
    save_json(counter_path, data)
    return f"{prefix}_{date_key}_{next_no:03d}"

# ===== PEF via DAISY Pipeline 2 =====
def resolve_pipeline_cmd(settings: dict, script_dir: Path) -> str:
    cmd = (settings.get("DAISY_PIPELINE_CMD") or "").strip()
    if cmd:
        p = Path(cmd)
        if p.exists():
            return str(p)
        p2 = script_dir / cmd
        if p2.exists():
            return str(p2)
        return cmd
    if (script_dir / "dp2.exe").exists():
        return str(script_dir / "dp2.exe")
    return "pipeline2"

def run_pipeline(pipeline_cmd: str, args: list[str]):
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
    shutil.copy2(pefs[0], pef_path)

def _text_to_simple_html(text: str, lang: str = "") -> str:
    paras = [p.strip() for p in text.split("\n\n") if p.strip()]
    parts = []
    parts.append("<!DOCTYPE html>")
    parts.append("<html" + (f' lang="{html.escape(lang)}"' if lang else "") + ">")
    parts.append("<head><meta charset=\"utf-8\"/></head><body>")
    for p in paras:
        parts.append(f"<p>{html.escape(p)}</p>")
    parts.append("</body></html>")
    return "\n".join(parts)

def make_pef_from_txt(text_path: Path, pef_path: Path, pipeline_cmd: str, braille_table: str, work: Path, lang: str = ""):
    pef_out = work / "pef_out"
    pef_out.mkdir(parents=True, exist_ok=True)

    text = text_path.read_text(encoding="utf-8", errors="ignore")
    html_path = work / "input.html"
    html_path.write_text(_text_to_simple_html(text, lang=lang), encoding="utf-8")

    run_pipeline(pipeline_cmd, [
        "html-to-pef",
        "--source", str(html_path),
        "--braille-code", f"(liblouis-table:{braille_table})",
        "-o", str(pef_out)
    ])

    pefs = list(pef_out.rglob("*.pef"))
    if not pefs:
        raise FileNotFoundError("Pipeline lavede ingen .pef (html-to-pef)")
    shutil.copy2(pefs[0], pef_path)

# ===== Processing =====
def process_one_file(input_file: Path, *,
                     api_key: str,
                     voice_id: str,
                     voice_name: str,
                     model_id: str,
                     iso_cmd: str,
                     max_tts_chars: int,
                     cache_dir: Path,
                     use_tts_cache: bool,
                     lang: str,
                     mode: str,
                     settings: dict,
                     meta_template: dict,
                     script_dir: Path):

    make_daisy = (mode in ("daisy", "both"))
    make_braille = (mode in ("braille", "both"))

    work = Path(tempfile.mkdtemp(prefix="daisy_work_"))
    try:
        book_name = input_file.stem
        volume_label = next_volume_label(script_dir, settings)

        text_file = work / "input.txt"
        docx_to_text(input_file, text_file)
        text = text_file.read_text(encoding="utf-8", errors="ignore")
        paragraphs = split_text_into_chunks(text, max_tts_chars)

        # ISO path (kun hvis DAISY)
        output_iso = input_file.with_suffix(".iso")
        output_iso = _choose_writable_output_path(output_iso)

        # --- Metadata CSV ---
        length_str = f"{len(text)} chars"
        iso_name_for_csv = ""
        if make_daisy:
            iso_name_for_csv = output_iso.name

        headers, row = prompt_metadata(
            meta_template,
            title_default=book_name,
            voice_name=voice_name,
            lang=lang,
            original_names=[input_file.name],
            iso_name=iso_name_for_csv,
            volume_label=volume_label,
            length_str=length_str
        )
        output_csv = input_file.with_suffix(".csv")
        write_metadata_csv(headers, row, output_csv)
        print(f"[{input_file.name}] CSV: {output_csv}")

        # --- Braille/PEF ---
        if make_braille:
            tables = settings.get("BRAILLE_TABLE_BY_LANG") or {}
            braille_table = (tables.get(lang) or "").strip()
            if not braille_table:
                print(f"[{input_file.name}] PEF springes over (ingen BRAILLE_TABLE_BY_LANG for '{lang}').")
            else:
                pipeline_cmd = resolve_pipeline_cmd(settings, script_dir)
                out_pef = input_file.with_suffix(".pef")
                try:
                    if input_file.suffix.lower() == ".docx":
                        make_pef_from_docx(input_file, out_pef, pipeline_cmd, braille_table, work)
                    else:
                        make_pef_from_txt(text_file, out_pef, pipeline_cmd, braille_table, work, lang=lang)
                    print(f"[{input_file.name}] PEF: {out_pef}")
                except Exception as e:
                    print(f"[{input_file.name}] PEF fejl: {e}")

        # --- DAISY/ISO ---
        if make_daisy:
            audio_dir = work / "audio"
            audio_dir.mkdir(parents=True, exist_ok=True)

            for i, chunk in enumerate(paragraphs, start=1):
                mp3_path = audio_dir / f"chapter_{i:03}.mp3"
                if use_tts_cache:
                    cpath = tts_cache_path(cache_dir, voice_id, model_id, chunk)
                    if cpath.exists():
                        shutil.copy2(cpath, mp3_path)
                        continue
                mp3_bytes = elevenlabs_tts_mp3(api_key, voice_id, model_id, chunk)
                mp3_path.write_bytes(mp3_bytes)
                if use_tts_cache:
                    cpath.parent.mkdir(parents=True, exist_ok=True)
                    cpath.write_bytes(mp3_bytes)

            # opdater length hvis muligt (best effort)
            try:
                from mutagen.mp3 import MP3  # optional
                total_sec = 0.0
                for mp in sorted(audio_dir.glob("chapter_*.mp3")):
                    total_sec += float(MP3(str(mp)).info.length)
                # overskriv "Lengte" i CSV hvis felt findes
                if headers and row:
                    for idx, h in enumerate(headers):
                        if h.lower().startswith("lengte"):
                            row[idx] = f"{total_sec:.1f}s"
                    write_metadata_csv(headers, row, output_csv)
            except Exception:
                pass

            daisy_dir = work / volume_label
            build_simple_daisy(volume_label, audio_dir, daisy_dir, paragraphs, lang=lang)

            ncc = daisy_dir / "ncc.html"
            if not ncc.exists():
                raise FileNotFoundError(f"ncc.html mangler: {ncc}")

            print(f"[{input_file.name}] Laver ISO ...")
            create_iso(iso_cmd, daisy_dir, output_iso, volume_name=volume_label)
            print(f"FÆRDIG: {output_iso}")

    finally:
        shutil.rmtree(work, ignore_errors=True)

def main():
    script_dir = Path(__file__).resolve().parent
    secrets_path = script_dir / "secrets.json"
    settings_path = script_dir / "settings.json"
    voices_path = script_dir / "voices.json"
    meta_path = script_dir / "metadata daisy.txt"

    secrets = load_or_create_secrets(secrets_path)
    settings = load_optional_json(settings_path, default={})
    meta_template = load_metadata_template(Path(settings.get("METADATA_TEMPLATE") or meta_path))

    maybe_update_voices_via_powershell(script_dir, secrets_path, voices_path)

    raw_voices = load_optional_json(voices_path, default=None)
    voices = normalize_voices(raw_voices)
    if not voices:
        print("ADVARSEL: voices.json mangler/er tom. Henter stemmer fra ElevenLabs API (fallback).")
        voices = fetch_voices_from_api(secrets["ELEVEN_API_KEY"])

    # vælg sprog + mode
    lang = choose_language_from_voices(voices)
    mode = choose_output_mode()

    # filtrér stemmer efter sprog (hvis ikke auto)
    voices_for_lang = voices
    if lang != "auto":
        vf = [v for v in voices if lang in _voice_langs(v)]
        if vf:
            voices_for_lang = vf

    selected_voice = choose_from_list(
        "Vælg stemme:",
        voices_for_lang,
        lambda v: f"{v.get('name','(uden navn)')} (voice_id: {get_voice_id(v)})"
    )
    voice_id = get_voice_id(selected_voice)
    voice_name = selected_voice.get("name","")

    # afled sprog hvis auto
    if lang == "auto":
        vl = _voice_langs(selected_voice)
        lang = vl[0] if vl else ""

    # paths/settings
    api_key = (secrets.get("ELEVEN_API_KEY") or "").strip()
    if not api_key:
        raise RuntimeError("ELEVEN_API_KEY mangler i secrets.json")

    default_root = Path(settings.get("DEFAULT_ROOT") or DEFAULT_ROOT)
    model_id = (settings.get("MODEL_ID") or DEFAULT_MODEL_ID).strip()
    iso_cmd = (settings.get("ISO_CMD") or "powershell-imapi2").strip()
    max_tts_chars = int(settings.get("MAX_TTS_CHARS") or DEFAULT_MAX_TTS_CHARS)
    use_tts_cache = bool(settings.get("USE_TTS_CACHE", True))
    cache_dir = (script_dir / (settings.get("CACHE_DIR") or "tts_cache")).resolve()
    cache_dir.mkdir(parents=True, exist_ok=True)

    # vælg projektmappe og fil
    if not default_root.exists():
        print(f"ADVARSEL: DEFAULT_ROOT findes ikke: {default_root}")
        root_str = input("Indtast sti til projekt-root: ").strip().strip('"')
        default_root = Path(root_str)

    folders = list_subfolders(default_root)
    if not folders:
        raise FileNotFoundError(f"Ingen undermapper i {default_root}")

    project = choose_from_list(f"Vælg mappe i {default_root}:", folders, lambda p: p.name)
    files = list_files(project)
    if not files:
        raise FileNotFoundError(f"Ingen .txt/.docx i {project}")

    print(f"\nVælg fil i {project}:")
    for i,f in enumerate(files):
        print(f"[{i}] {f.name}")
    print("[a] Alle filer")

    choice = (input("Vælg nummer eller 'a': ") or "").strip().lower()
    if choice == "a":
        selected_files = files
    else:
        if not choice.isdigit():
            raise ValueError("Ugyldigt valg.")
        idx = int(choice)
        if idx < 0 or idx >= len(files):
            raise ValueError("Ugyldigt valg.")
        selected_files = [files[idx]]

    for f in selected_files:
        process_one_file(
            f,
            api_key=api_key,
            voice_id=voice_id,
            voice_name=voice_name,
            model_id=model_id,
            iso_cmd=iso_cmd,
            max_tts_chars=max_tts_chars,
            cache_dir=cache_dir,
            use_tts_cache=use_tts_cache,
            lang=lang,
            mode=mode,
            settings=settings,
            meta_template=meta_template,
            script_dir=script_dir
        )

    input("\nTryk Enter for at afslutte...")

if __name__ == "__main__":
    try:
        main()
    except Exception as e:
        print("\nFEJL:", e)
        input("Tryk Enter for at afslutte...")
        raise
