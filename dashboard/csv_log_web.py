import argparse
import csv
import io
import json
import socket
import zipfile
from datetime import datetime
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, quote, urlparse
from xml.sax.saxutils import escape as xml_escape

HTML_PAGE = """<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Mitsubishi CNC Log Table</title>
  <style>
    :root {
      --bg: #f4efe7;
      --panel: #ffffff;
      --line: #d7dde7;
      --ink: #1f2f48;
      --muted: #6a7b92;
      --accent: #0f6a7a;
      --accent-2: #c28a2d;
      --ok: #1f7a41;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      font-family: "Segoe UI", sans-serif;
      background: linear-gradient(180deg, #fbf8f2 0%, var(--bg) 100%);
      color: var(--ink);
    }
    .shell {
      max-width: 1600px;
      margin: 0 auto;
      padding: 20px;
    }
    .hero {
      background: linear-gradient(90deg, #0f4d59 0%, #c28a2d 100%);
      color: #fff;
      border-radius: 20px;
      padding: 22px 24px;
      margin-bottom: 18px;
      box-shadow: 0 16px 40px rgba(16, 30, 54, 0.12);
    }
    .hero h1 {
      margin: 0 0 8px;
      font-size: 32px;
      line-height: 1.1;
    }
    .hero p {
      margin: 0;
      color: rgba(255,255,255,0.92);
      max-width: 860px;
    }
    .panel {
      background: var(--panel);
      border: 1px solid var(--line);
      border-radius: 18px;
      padding: 18px;
      margin-bottom: 16px;
      box-shadow: 0 8px 24px rgba(16, 30, 54, 0.05);
    }
    .toolbar {
      display: grid;
      grid-template-columns: minmax(260px, 1.8fr) minmax(140px, 0.8fr) minmax(200px, 1fr) auto auto auto auto;
      gap: 10px;
      align-items: end;
    }
    .field label {
      display: block;
      margin-bottom: 6px;
      font-size: 12px;
      color: var(--muted);
      font-weight: 600;
      letter-spacing: 0.02em;
      text-transform: uppercase;
    }
    .field input, .field select {
      width: 100%;
      height: 42px;
      border: 1px solid var(--line);
      border-radius: 10px;
      padding: 0 12px;
      font-size: 14px;
      background: #fff;
      color: var(--ink);
    }
    button, .button-link {
      height: 42px;
      border: 0;
      border-radius: 10px;
      padding: 0 16px;
      font-weight: 700;
      cursor: pointer;
      font-size: 14px;
      text-decoration: none;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      white-space: nowrap;
    }
    .primary { background: var(--accent); color: #fff; }
    .secondary { background: #eef3f7; color: var(--ink); }
    .download { background: var(--ok); color: #fff; }
    .info-grid {
      display: grid;
      grid-template-columns: repeat(4, minmax(0, 1fr));
      gap: 12px;
      margin-top: 14px;
    }
    .stat {
      border: 1px solid var(--line);
      border-radius: 14px;
      padding: 12px 14px;
      background: #fafbfd;
    }
    .stat .label {
      font-size: 12px;
      color: var(--muted);
      margin-bottom: 6px;
      text-transform: uppercase;
      letter-spacing: 0.03em;
    }
    .stat .value {
      font-size: 16px;
      font-weight: 700;
      word-break: break-word;
    }
    .table-shell {
      overflow: auto;
      max-height: calc(100vh - 310px);
      border: 1px solid var(--line);
      border-radius: 14px;
      background: #fff;
    }
    table {
      width: max-content;
      min-width: 100%;
      border-collapse: collapse;
      font-size: 13px;
    }
    th, td {
      border-bottom: 1px solid #edf1f6;
      border-right: 1px solid #edf1f6;
      padding: 10px 12px;
      text-align: left;
      white-space: nowrap;
    }
    th {
      position: sticky;
      top: 0;
      z-index: 2;
      background: #edf3f7;
      color: #24364f;
      font-size: 12px;
      text-transform: uppercase;
      letter-spacing: 0.03em;
    }
    tr:nth-child(even) td {
      background: #fbfcfd;
    }
    .pager {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 12px;
      margin-top: 14px;
      flex-wrap: wrap;
    }
    .muted { color: var(--muted); }
    .empty {
      padding: 22px;
      text-align: center;
      color: var(--muted);
      font-weight: 600;
    }
    @media (max-width: 1200px) {
      .toolbar {
        grid-template-columns: 1fr 1fr;
      }
      .info-grid {
        grid-template-columns: 1fr 1fr;
      }
    }
    @media (max-width: 720px) {
      .hero h1 { font-size: 24px; }
      .toolbar, .info-grid {
        grid-template-columns: 1fr;
      }
      .table-shell {
        max-height: none;
      }
    }
  </style>
</head>
<body>
  <div class="shell">
    <div class="hero">
      <h1>CSV Log Web Table</h1>
      <p>Open the CNC CSV logs in a browser, share the link on your network, and download the same data as CSV or Excel.</p>
    </div>

    <div class="panel">
      <div class="toolbar">
        <div class="field">
          <label for="fileSelect">Log File</label>
          <select id="fileSelect"></select>
        </div>
        <div class="field">
          <label for="pageSize">Rows Per Page</label>
          <select id="pageSize">
            <option>50</option>
            <option selected>100</option>
            <option>250</option>
            <option>500</option>
          </select>
        </div>
        <div class="field">
          <label for="searchBox">Search</label>
          <input id="searchBox" type="text" placeholder="tool, alarm, rpm, axis, mode...">
        </div>
        <button id="refreshButton" class="secondary" type="button">Refresh</button>
        <button id="shareButton" class="primary" type="button">Copy Share Link</button>
        <a id="downloadCsv" class="button-link secondary" href="#">Download CSV</a>
        <a id="downloadXlsx" class="button-link download" href="#">Download Excel</a>
      </div>

      <div class="info-grid">
        <div class="stat">
          <div class="label">Log Directory</div>
          <div class="value" id="logDir">--</div>
        </div>
        <div class="stat">
          <div class="label">Selected File</div>
          <div class="value" id="fileInfo">--</div>
        </div>
        <div class="stat">
          <div class="label">Rows</div>
          <div class="value" id="rowInfo">--</div>
        </div>
        <div class="stat">
          <div class="label">Share URL</div>
          <div class="value" id="shareInfo">--</div>
        </div>
      </div>
    </div>

    <div class="panel">
      <div class="table-shell">
        <table id="logTable">
          <thead></thead>
          <tbody></tbody>
        </table>
        <div class="empty" id="emptyState">Loading log table...</div>
      </div>
      <div class="pager">
        <div class="muted" id="pageInfo">--</div>
        <div>
          <button id="prevButton" class="secondary" type="button">Previous</button>
          <button id="nextButton" class="secondary" type="button">Next</button>
        </div>
      </div>
    </div>
  </div>

  <script>
    const state = {
      file: "",
      page: 1,
      pageSize: 100,
      search: ""
    };

    const fileSelect = document.getElementById("fileSelect");
    const pageSize = document.getElementById("pageSize");
    const searchBox = document.getElementById("searchBox");
    const refreshButton = document.getElementById("refreshButton");
    const prevButton = document.getElementById("prevButton");
    const nextButton = document.getElementById("nextButton");
    const shareButton = document.getElementById("shareButton");
    const downloadCsv = document.getElementById("downloadCsv");
    const downloadXlsx = document.getElementById("downloadXlsx");
    const tableHead = document.querySelector("#logTable thead");
    const tableBody = document.querySelector("#logTable tbody");
    const emptyState = document.getElementById("emptyState");
    const logDir = document.getElementById("logDir");
    const fileInfo = document.getElementById("fileInfo");
    const rowInfo = document.getElementById("rowInfo");
    const shareInfo = document.getElementById("shareInfo");
    const pageInfo = document.getElementById("pageInfo");

    function currentShareUrl() {
      const url = new URL(window.location.href);
      if (state.file) {
        url.searchParams.set("file", state.file);
      }
      if (state.search) {
        url.searchParams.set("q", state.search);
      } else {
        url.searchParams.delete("q");
      }
      url.searchParams.set("page_size", String(state.pageSize));
      return url.toString();
    }

    function syncUrl() {
      window.history.replaceState({}, "", currentShareUrl());
      shareInfo.textContent = currentShareUrl();
    }

    function updateDownloadLinks() {
      const fileParam = encodeURIComponent(state.file || "");
      downloadCsv.href = "/download/csv?file=" + fileParam;
      downloadXlsx.href = "/download/xlsx?file=" + fileParam;
    }

    async function loadFiles() {
      const response = await fetch("/api/files");
      const payload = await response.json();
      logDir.textContent = payload.log_dir || "--";

      const previous = state.file;
      fileSelect.innerHTML = "";

      if (!payload.files.length) {
        emptyState.textContent = "No CSV log files were found in the selected log directory.";
        emptyState.style.display = "block";
        return;
      }

      for (const entry of payload.files) {
        const option = document.createElement("option");
        option.value = entry.name;
        option.textContent = `${entry.name} (${entry.modified_local})`;
        fileSelect.appendChild(option);
      }

      const urlFile = new URL(window.location.href).searchParams.get("file");
      state.file = payload.files.some(item => item.name === previous) ? previous : "";
      if (!state.file && urlFile && payload.files.some(item => item.name === urlFile)) {
        state.file = urlFile;
      }
      if (!state.file) {
        state.file = payload.files[0].name;
      }

      fileSelect.value = state.file;
      updateDownloadLinks();
      syncUrl();
      await loadTable();
    }

    async function loadTable() {
      if (!state.file) {
        return;
      }

      const params = new URLSearchParams({
        file: state.file,
        page: String(state.page),
        page_size: String(state.pageSize),
        q: state.search
      });

      const response = await fetch("/api/table?" + params.toString());
      const payload = await response.json();

      tableHead.innerHTML = "";
      tableBody.innerHTML = "";

      fileInfo.textContent = `${payload.file_name} | ${payload.modified_local}`;
      rowInfo.textContent = `${payload.total_rows} matching rows`;
      pageInfo.textContent = `Page ${payload.page} of ${payload.total_pages}`;
      updateDownloadLinks();
      syncUrl();

      if (!payload.headers.length || !payload.rows.length) {
        emptyState.textContent = payload.total_rows === 0
          ? "No rows matched the current search."
          : "This log file is empty.";
        emptyState.style.display = "block";
      } else {
        emptyState.style.display = "none";
      }

      if (payload.headers.length) {
        const headerRow = document.createElement("tr");
        for (const header of payload.headers) {
          const th = document.createElement("th");
          th.textContent = header;
          headerRow.appendChild(th);
        }
        tableHead.appendChild(headerRow);
      }

      for (const row of payload.rows) {
        const tr = document.createElement("tr");
        for (const cell of row) {
          const td = document.createElement("td");
          td.textContent = cell;
          tr.appendChild(td);
        }
        tableBody.appendChild(tr);
      }

      prevButton.disabled = payload.page <= 1;
      nextButton.disabled = payload.page >= payload.total_pages;
    }

    fileSelect.addEventListener("change", async () => {
      state.file = fileSelect.value;
      state.page = 1;
      await loadTable();
    });

    pageSize.addEventListener("change", async () => {
      state.pageSize = Number(pageSize.value);
      state.page = 1;
      await loadTable();
    });

    searchBox.addEventListener("change", async () => {
      state.search = searchBox.value.trim();
      state.page = 1;
      await loadTable();
    });

    refreshButton.addEventListener("click", async () => {
      await loadFiles();
    });

    prevButton.addEventListener("click", async () => {
      if (state.page > 1) {
        state.page -= 1;
        await loadTable();
      }
    });

    nextButton.addEventListener("click", async () => {
      state.page += 1;
      await loadTable();
    });

    shareButton.addEventListener("click", async () => {
      const url = currentShareUrl();
      try {
        await navigator.clipboard.writeText(url);
        shareButton.textContent = "Link Copied";
      } catch {
        shareButton.textContent = "Copy Failed";
      }
      setTimeout(() => {
        shareButton.textContent = "Copy Share Link";
      }, 1600);
    });

    (async () => {
      const url = new URL(window.location.href);
      const pageSizeParam = Number(url.searchParams.get("page_size") || "100");
      const searchParam = url.searchParams.get("q") || "";
      if ([50, 100, 250, 500].includes(pageSizeParam)) {
        state.pageSize = pageSizeParam;
        pageSize.value = String(pageSizeParam);
      }
      state.search = searchParam;
      searchBox.value = searchParam;
      await loadFiles();
    })();
  </script>
</body>
</html>
"""


def find_default_log_dir() -> Path:
    repo_root = Path(__file__).resolve().parent.parent
    candidates = [
        repo_root / "build" / "publish" / "MitsubishiCncMonitor" / "logs",
        repo_root / "logs",
        Path.cwd() / "logs",
    ]

    for candidate in candidates:
        if candidate.exists() and any(candidate.glob("*.csv")):
            return candidate

    for candidate in candidates:
        if candidate.exists():
            return candidate

    return candidates[0]


def format_local_timestamp(ts: float) -> str:
    return datetime.fromtimestamp(ts).strftime("%Y-%m-%d %H:%M:%S")


def scan_csv_files(log_dir: Path) -> list[Path]:
    if not log_dir.exists():
        return []
    return sorted((path for path in log_dir.glob("*.csv") if path.is_file()), key=lambda item: item.stat().st_mtime, reverse=True)


def resolve_csv_path(log_dir: Path, file_name: str | None) -> Path:
    files = scan_csv_files(log_dir)
    if not files:
        raise FileNotFoundError("No CSV files were found.")

    if not file_name:
        return files[0]

    safe_name = Path(file_name).name
    path = log_dir / safe_name
    if not path.exists() or path.suffix.lower() != ".csv":
        raise FileNotFoundError(f"CSV file was not found: {safe_name}")
    return path


def read_csv_page(csv_path: Path, page: int, page_size: int, search: str) -> dict:
    headers: list[str] = []
    rows: list[list[str]] = []
    total_rows = 0
    start_index = max(0, (page - 1) * page_size)
    end_index = start_index + page_size
    search_text = search.casefold().strip()

    with csv_path.open("r", newline="", encoding="utf-8-sig") as handle:
        reader = csv.DictReader(handle)
        headers = reader.fieldnames or []
        for row in reader:
            values = [row.get(header, "") for header in headers]
            if search_text and search_text not in " ".join(values).casefold():
                continue

            if start_index <= total_rows < end_index:
                rows.append(values)
            total_rows += 1

    total_pages = max(1, (total_rows + page_size - 1) // page_size)
    return {
        "headers": headers,
        "rows": rows,
        "total_rows": total_rows,
        "total_pages": total_pages,
    }


def read_csv_all(csv_path: Path) -> tuple[list[str], list[list[str]]]:
    with csv_path.open("r", newline="", encoding="utf-8-sig") as handle:
        reader = csv.DictReader(handle)
        headers = reader.fieldnames or []
        rows = [[row.get(header, "") for header in headers] for row in reader]
    return headers, rows


def column_letter(index: int) -> str:
    letters = []
    while index > 0:
        index, remainder = divmod(index - 1, 26)
        letters.append(chr(65 + remainder))
    return "".join(reversed(letters))


def build_xlsx_bytes(headers: list[str], rows: list[list[str]], sheet_name: str) -> bytes:
    workbook_rows = [headers] + rows
    last_row = max(1, len(workbook_rows))
    last_col = max(1, len(headers) if headers else 1)
    last_ref = f"{column_letter(last_col)}{last_row}"
    sheet_rows_xml: list[str] = []

    for row_index, values in enumerate(workbook_rows, start=1):
        cell_xml: list[str] = []
        for col_index in range(1, max(last_col, len(values)) + 1):
            value = values[col_index - 1] if col_index - 1 < len(values) else ""
            style_id = "1" if row_index == 1 else "0"
            ref = f"{column_letter(col_index)}{row_index}"
            escaped = xml_escape(str(value))
            cell_xml.append(
                f'<c r="{ref}" s="{style_id}" t="inlineStr"><is><t xml:space="preserve">{escaped}</t></is></c>'
            )
        sheet_rows_xml.append(f'<row r="{row_index}">{"".join(cell_xml)}</row>')

    safe_sheet_name = xml_escape((sheet_name[:31] or "Log").replace("/", "_").replace("\\", "_"))
    worksheet_xml = f"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <dimension ref="A1:{last_ref}"/>
  <sheetViews>
    <sheetView workbookViewId="0">
      <pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/>
      <selection pane="bottomLeft" activeCell="A2" sqref="A2"/>
    </sheetView>
  </sheetViews>
  <sheetFormatPr defaultRowHeight="15"/>
  <sheetData>
    {"".join(sheet_rows_xml)}
  </sheetData>
  <autoFilter ref="A1:{last_ref}"/>
</worksheet>
"""

    content_types_xml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
  <Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
  <Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>
</Types>
"""

    root_rels_xml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/>
</Relationships>
"""

    workbook_xml = f"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <sheets>
    <sheet name="{safe_sheet_name}" sheetId="1" r:id="rId1"/>
  </sheets>
</workbook>
"""

    workbook_rels_xml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
</Relationships>
"""

    styles_xml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <fonts count="2">
    <font><sz val="11"/><name val="Calibri"/><family val="2"/></font>
    <font><b/><sz val="11"/><name val="Calibri"/><family val="2"/></font>
  </fonts>
  <fills count="2">
    <fill><patternFill patternType="none"/></fill>
    <fill><patternFill patternType="gray125"/></fill>
  </fills>
  <borders count="1">
    <border><left/><right/><top/><bottom/><diagonal/></border>
  </borders>
  <cellStyleXfs count="1">
    <xf numFmtId="0" fontId="0" fillId="0" borderId="0"/>
  </cellStyleXfs>
  <cellXfs count="2">
    <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
    <xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/>
  </cellXfs>
  <cellStyles count="1">
    <cellStyle name="Normal" xfId="0" builtinId="0"/>
  </cellStyles>
</styleSheet>
"""

    created_iso = datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ")
    core_xml = f"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
                   xmlns:dc="http://purl.org/dc/elements/1.1/"
                   xmlns:dcterms="http://purl.org/dc/terms/"
                   xmlns:dcmitype="http://purl.org/dc/dcmitype/"
                   xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <dc:title>Mitsubishi CNC CSV Log</dc:title>
  <dc:creator>OpenAI Codex</dc:creator>
  <cp:lastModifiedBy>OpenAI Codex</cp:lastModifiedBy>
  <dcterms:created xsi:type="dcterms:W3CDTF">{created_iso}</dcterms:created>
  <dcterms:modified xsi:type="dcterms:W3CDTF">{created_iso}</dcterms:modified>
</cp:coreProperties>
"""

    app_xml = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"
            xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
  <Application>Microsoft Excel</Application>
</Properties>
"""

    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        archive.writestr("[Content_Types].xml", content_types_xml)
        archive.writestr("_rels/.rels", root_rels_xml)
        archive.writestr("xl/workbook.xml", workbook_xml)
        archive.writestr("xl/_rels/workbook.xml.rels", workbook_rels_xml)
        archive.writestr("xl/worksheets/sheet1.xml", worksheet_xml)
        archive.writestr("xl/styles.xml", styles_xml)
        archive.writestr("docProps/core.xml", core_xml)
        archive.writestr("docProps/app.xml", app_xml)
    return buffer.getvalue()


class AppContext:
    def __init__(self, log_dir: Path):
        self.log_dir = log_dir


class LogRequestHandler(BaseHTTPRequestHandler):
    server_version = "CncCsvLogWeb/1.0"

    @property
    def context(self) -> AppContext:
        return self.server.context  # type: ignore[attr-defined]

    def do_GET(self) -> None:
        parsed = urlparse(self.path)
        try:
            if parsed.path == "/":
                self.serve_html()
                return
            if parsed.path == "/api/files":
                self.serve_files()
                return
            if parsed.path == "/api/table":
                self.serve_table(parse_qs(parsed.query))
                return
            if parsed.path == "/download/csv":
                self.serve_csv(parse_qs(parsed.query))
                return
            if parsed.path == "/download/xlsx":
                self.serve_xlsx(parse_qs(parsed.query))
                return
            self.send_error(404, "Not found")
        except FileNotFoundError as exc:
            self.send_json({"error": str(exc)}, status=404)
        except Exception as exc:  # pragma: no cover - defensive
            self.send_json({"error": str(exc)}, status=500)

    def log_message(self, fmt: str, *args) -> None:
        return

    def serve_html(self) -> None:
        body = HTML_PAGE.encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def serve_files(self) -> None:
        files = []
        for path in scan_csv_files(self.context.log_dir):
            stat = path.stat()
            files.append(
                {
                    "name": path.name,
                    "size_bytes": stat.st_size,
                    "modified_local": format_local_timestamp(stat.st_mtime),
                }
            )
        self.send_json({"log_dir": str(self.context.log_dir.resolve()), "files": files})

    def serve_table(self, query: dict[str, list[str]]) -> None:
        file_name = query.get("file", [""])[0]
        page = max(1, int(query.get("page", ["1"])[0]))
        page_size = max(1, min(1000, int(query.get("page_size", ["100"])[0])))
        search = query.get("q", [""])[0]

        csv_path = resolve_csv_path(self.context.log_dir, file_name)
        payload = read_csv_page(csv_path, page, page_size, search)
        stat = csv_path.stat()
        payload.update(
            {
                "file_name": csv_path.name,
                "modified_local": format_local_timestamp(stat.st_mtime),
                "page": page,
                "page_size": page_size,
            }
        )
        self.send_json(payload)

    def serve_csv(self, query: dict[str, list[str]]) -> None:
        csv_path = resolve_csv_path(self.context.log_dir, query.get("file", [""])[0])
        data = csv_path.read_bytes()
        self.send_response(200)
        self.send_header("Content-Type", "text/csv; charset=utf-8")
        self.send_header("Content-Disposition", f'attachment; filename="{csv_path.name}"')
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def serve_xlsx(self, query: dict[str, list[str]]) -> None:
        csv_path = resolve_csv_path(self.context.log_dir, query.get("file", [""])[0])
        headers, rows = read_csv_all(csv_path)
        data = build_xlsx_bytes(headers, rows, sheet_name=csv_path.stem)
        download_name = f"{csv_path.stem}.xlsx"
        self.send_response(200)
        self.send_header(
            "Content-Type",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        )
        self.send_header("Content-Disposition", f'attachment; filename="{download_name}"')
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def send_json(self, payload: dict, status: int = 200) -> None:
        body = json.dumps(payload).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Serve Mitsubishi CNC CSV logs as a web table.")
    parser.add_argument("--host", default="127.0.0.1", help="Host to bind. Use 0.0.0.0 to share on your network.")
    parser.add_argument("--port", type=int, default=8080, help="TCP port for the web server.")
    parser.add_argument("--log-dir", type=Path, default=find_default_log_dir(), help="Directory containing CSV log files.")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    args.log_dir.mkdir(parents=True, exist_ok=True)

    server = ThreadingHTTPServer((args.host, args.port), LogRequestHandler)
    server.context = AppContext(args.log_dir)  # type: ignore[attr-defined]

    host_for_display = args.host
    if args.host == "0.0.0.0":
        try:
            host_for_display = socket.gethostbyname(socket.gethostname())
        except OSError:
            host_for_display = "localhost"

    print(f"Serving CSV log table from {args.log_dir}")
    print(f"Open: http://{host_for_display}:{args.port}/")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
