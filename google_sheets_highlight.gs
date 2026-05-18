function onSelectionChange(e) {
  if (!e || !e.range) return;

  var config = {
    sheetName: "Sheet1",
    summaryStartRow: 2,
    summaryCoreCol: 23,    // W
    summarySqmmCol: 24,    // X
    summaryColourCol: 25,  // Y
    summaryTotalCol: 26,   // Z
    sourceStartRow: 2,
    sourcePowerCol: 3,     // C
    sourceControlCol: 4,   // D
    sourceColourCol: 5,    // E
    sourceHighlight: "#fff2cc",
    totalHighlight: "#f9cb9c"
  };

  var sheet = e.range.getSheet();
  if (sheet.getName() !== config.sheetName) return;

  var props = PropertiesService.getDocumentProperties();
  var token = Utilities.getUuid();
  props.setProperty("selection_request_token", token);

  clearPreviousHighlights_(sheet, props);

  if (e.range.getColumn() !== config.summaryTotalCol || e.range.getRow() < config.summaryStartRow) {
    return;
  }

  var totalA1 = e.range.getA1Notation();
  sheet.getRange(totalA1).setBackground(config.totalHighlight);
  saveHighlightState_(props, sheet.getName(), totalA1, []);

  var selectedRow = e.range.getRow();
  var selectedCore = Number(sheet.getRange(selectedRow, config.summaryCoreCol).getValue());
  var selectedSqmm = Number(sheet.getRange(selectedRow, config.summarySqmmCol).getValue());
  var selectedColour = normalize_(sheet.getRange(selectedRow, config.summaryColourCol).getDisplayValue());

  if (!selectedCore || !selectedSqmm || !selectedColour) return;

  var lastRow = sheet.getLastRow();
  var rowCount = Math.max(0, lastRow - config.sourceStartRow + 1);
  if (!rowCount) return;

  var sourceValues = sheet
    .getRange(config.sourceStartRow, config.sourcePowerCol, rowCount, 3)
    .getDisplayValues();

  var cellsToHighlight = [];

  for (var i = 0; i < sourceValues.length; i++) {
    var rowNumber = config.sourceStartRow + i;
    var powerLines = splitLines_(sourceValues[i][0]);
    var controlLines = splitLines_(sourceValues[i][1]);
    var colourLines = splitLines_(sourceValues[i][2]);

    if (matchesAnyLine_(powerLines, colourLines, 0, selectedCore, selectedSqmm, selectedColour)) {
      cellsToHighlight.push("C" + rowNumber);
    }

    if (matchesAnyLine_(controlLines, colourLines, powerLines.length, selectedCore, selectedSqmm, selectedColour)) {
      cellsToHighlight.push("D" + rowNumber);
    }
  }

  if (props.getProperty("selection_request_token") !== token) return;

  var uniqueA1s = Array.from(new Set(cellsToHighlight));
  if (uniqueA1s.length) {
    sheet.getRangeList(uniqueA1s).setBackground(config.sourceHighlight);
  }

  saveHighlightState_(props, sheet.getName(), totalA1, uniqueA1s);
}

function matchesAnyLine_(specLines, colourLines, colourOffset, selectedCore, selectedSqmm, selectedColour) {
  for (var i = 0; i < specLines.length; i++) {
    var spec = specLines[i];
    var colour = colourLines[colourOffset + i] || "";
    if (lineMatches_(spec, colour, selectedCore, selectedSqmm, selectedColour)) {
      return true;
    }
  }
  return false;
}

function lineMatches_(specText, colourText, selectedCore, selectedSqmm, selectedColour) {
  var parsed = parseSpec_(specText);
  if (!parsed) return false;

  var resolvedColour = resolveColour_(specText, colourText);
  if (!resolvedColour) return false;

  return parsed.core === selectedCore &&
    parsed.sqmm === selectedSqmm &&
    resolvedColour === selectedColour;
}

function parseSpec_(value) {
  var spec = normalize_(value);
  if (!spec) return null;

  var coreMatch = spec.match(/^(\d+)\s*CORE\b/);
  var sqmmMatch = spec.match(/\bCORE\s+(\d+(?:\.\d+)?)\s*SQM{1,2}\b/);

  if (!coreMatch || !sqmmMatch) return null;

  return {
    core: Number(coreMatch[1]),
    sqmm: Number(sqmmMatch[1])
  };
}

function resolveColour_(specText, colourText) {
  var explicitColour = normalize_(colourText);
  if (explicitColour) return explicitColour;

  var spec = normalize_(specText);
  if (!spec) return "";

  if (/\bFOR\s+R(?:-COLOUR)?\b/.test(spec)) return "RED";
  if (/\bFOR\s+Y(?:-COLOUR)?\b/.test(spec)) return "YELLOW";
  if (/\bFOR\s+B(?:-COLOUR)?\b/.test(spec)) return "BLUE";
  if (/\bFOR\s+N(?:-COLOUR)?\b/.test(spec)) return "BLACK";
  if (/\bFOR\s+E(?:-COLOUR)?\b/.test(spec)) return "EARTH";

  return "";
}

function splitLines_(value) {
  return String(value || "")
    .split(/\r?\n/)
    .map(function (line) { return normalize_(line); })
    .filter(function (line) { return line !== ""; });
}

function normalize_(value) {
  return String(value || "")
    .toUpperCase()
    .replace(/\s+/g, " ")
    .trim();
}

function clearPreviousHighlights_(sheet, props) {
  var raw = props.getProperty("selection_highlight_state");
  if (!raw) return;

  try {
    var state = JSON.parse(raw);
    if (state.sheetName === sheet.getName()) {
      var allA1s = [];

      if (state.totalA1) allA1s.push(state.totalA1);
      if (state.cells && state.cells.length) allA1s = allA1s.concat(state.cells);

      if (allA1s.length) {
        sheet.getRangeList(Array.from(new Set(allA1s))).setBackground(null);
      }
    }
  } catch (err) {
    // Ignore invalid saved state.
  }

  props.deleteProperty("selection_highlight_state");
}

function saveHighlightState_(props, sheetName, totalA1, cells) {
  props.setProperty(
    "selection_highlight_state",
    JSON.stringify({
      sheetName: sheetName,
      totalA1: totalA1,
      cells: cells || []
    })
  );
}

function clearSelectionHighlights() {
  var sheet = SpreadsheetApp.getActiveSpreadsheet().getSheetByName("Sheet1");
  if (!sheet) return;

  PropertiesService.getDocumentProperties().deleteProperty("selection_highlight_state");
  PropertiesService.getDocumentProperties().deleteProperty("selection_request_token");

  sheet.getRangeList(["C2:D1000", "Z2:Z1000"]).setBackground(null);
}
