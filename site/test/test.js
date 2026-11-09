import { problem } from "../settings.js";

const failures = [];
const frame = document.getElementById("app");
let win;
let doc;
let captured = null;

async function until(condition, what) {
  const deadline = performance.now() + 60000;
  while (!condition()) {
    if (performance.now() > deadline) {
      throw new Error(`timed out waiting for ${what}`);
    }

    await new Promise(resolve => setTimeout(resolve, 10));
  }
}

async function bytesOf(path) {
  const response = await fetch(path);
  if (!response.ok) {
    throw new Error(`${path}: ${response.status}`);
  }

  return new Uint8Array(await response.arrayBuffer());
}

const control = id => {
  const element = doc.getElementById(id);
  if (element === null) {
    throw new Error(`no control ${id}`);
  }

  return element;
};

function check(id, on) {
  const box = control(id);
  box.checked = on;
  box.dispatchEvent(new win.Event("change"));
}

function choose(id, label) {
  const select = control(id);
  const index = [...select.options].findIndex(option => option.textContent === label);
  if (index < 0) {
    throw new Error(`${id} has no choice "${label}"`);
  }

  select.selectedIndex = index;
  select.dispatchEvent(new win.Event("change"));
}

function write(id, text) {
  const input = control(id);
  input.value = text;
  input.dispatchEvent(new win.Event("input"));
}

function expectEqual(what, expected, actual) {
  if (expected !== actual) {
    failures.push(`${what}: expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
  }
}

async function open(name, bytes) {
  const file = new win.File([bytes], name);
  const transfer = new win.DataTransfer();
  transfer.items.add(file);
  const input = control("file");
  input.files = transfer.files;
  input.dispatchEvent(new win.Event("change"));
  await until(() => !control("editor").hidden || !control("legacy").hidden || !control("status").hidden, `${name} to open`);
  expectEqual(`${name} opened`, "", control("status").hidden ? "" : control("status").textContent);
}

async function download() {
  captured = null;
  control("download").click();
  await until(() => captured !== null, "the download");
  return new Uint8Array(await captured.arrayBuffer());
}

async function post(path, body) {
  const response = await fetch(path, { method: "POST", body });
  if (!response.ok) {
    throw new Error(`${path}: ${response.status}`);
  }
}

const editCases = {
  edit() {
    check("setting.set_game_time", false);
    control("setting.timing_method.chapter").click();
    choose("reset.kind", "Exit chapter");
    choose("split.0.kind", "Chapter complete");
    choose("split.0.field.timing", "Chapter exit");
    write("split.0.field.chapter", "Core");
    choose("split.1.kind", "Checkpoint");
    write("split.1.field.chapter", "Forsaken City");
    choose("split.1.field.checkpoint", "Chasm");
    expectEqual("the entry past the last segment isn't shown", null, doc.getElementById("split.2.kind"));
    expectEqual("sid is hidden with everest off", null, doc.getElementById("start.field.sid"));
    check("setting.everest", true);
    expectEqual("sid is shown with everest on", "Celeste/3-CelestialResort", control("start.field.sid").value);
  },
  insert() {
    control("preset.0").click();
  },
  compact() {
    check("setting.everest", true);
  },
  utf16() {
    choose("split.0.kind", "Enter room");
    write("split.0.field.room", "Café-日本");
  },
  comment() {
    check("setting.high_priority", false);
  },
};

async function editChecks() {
  for (const [name, edit] of Object.entries(editCases)) {
    await open(`${name}.lss`, await bytesOf(`cases/${name}.lss`));
    edit();
    await post(`/output/edit/${name}`, await download());
  }

  const input = await bytesOf("cases/edit.lss");
  await open("edit.lss", input);
  expectEqual("an unedited download is byte-identical", 0, compare(input, await download()));
}

function compare(a, b) {
  if (a.length !== b.length) {
    return 1;
  }

  return a.findIndex((byte, index) => byte !== b[index]) === -1 ? 0 : 1;
}

async function characterReferenceCheck() {
  const input = '<Run><AutoSplitterSettings><Note>a&#xD;b</Note><CustomSettings><Setting id="cfg" type="map"><Setting id="future" type="string" value="a&#xA;b&#x9;c&#xD;d" /></Setting></CustomSettings></AutoSplitterSettings></Run>';
  await open("references.lss", new TextEncoder().encode(input));
  check("setting.everest", true);
  const output = new DOMParser().parseFromString(new TextDecoder().decode(await download()), "text/xml");
  expectEqual("an attribute's line breaks and tabs are kept", "a\nb\tc\rd", output.querySelector('[id="future"]').getAttribute("value"));
  expectEqual("a carriage return in text is kept", "a\rb", output.querySelector("Note").textContent);
}

async function readOnlyCheck() {
  const input = await bytesOf("cases/newer.lss");
  await open("newer.lss", input);
  const warnings = control("warnings").textContent;
  if (!warnings.includes("newer")) {
    failures.push(`a newer schema_version doesn't say so: ${warnings}`);
  }

  expectEqual("no invalid timing method is flagged while read-only", false, warnings.includes("isn't valid"));
  const enabled = [...doc.querySelectorAll("#editor input, #editor select, #editor button")].filter(element => !element.disabled);
  expectEqual("a newer schema_version disables every control", "", enabled.map(element => element.id).join());
}

async function flagChecks() {
  await open("flags.lss", await bytesOf("cases/flags.lss"));
  expectEqual("invalid timing method", true, control("warnings").textContent.includes('Timing method "nonsense" isn\'t valid, so File is used.'));
  expectEqual("the top line says some are flagged", true, control("warnings").textContent.includes("invalid or never fire"));
  expectEqual("unknown", "This legacy split wasn't recognised, so it never fires.", control("start.problem").textContent);
  expectEqual("no room", "This trigger has no room, so it behaves like manual.", control("split.0.problem").textContent);
  expectEqual("no such checkpoint", "This trigger has an invalid checkpoint, so it behaves like manual.", control("split.1.problem").textContent);
  expectEqual("sid ignored with everest off", "", control("split.2.problem").textContent);
  expectEqual("no timing_method radio selected when invalid", false, control("setting.timing_method.file").checked);
  check("setting.everest", true);
  expectEqual("sid of the wrong type with everest on", "This trigger has an invalid chapter sid, so it behaves like manual.", control("split.2.problem").textContent);
  expectEqual("fine", "", control("split.3.problem").textContent);
}

function setting(document, id, entry) {
  const element = document.createElement("Setting");
  if (id !== null) {
    element.setAttribute("id", id);
  }

  if (typeof entry === "boolean") {
    element.setAttribute("type", "bool");
    element.textContent = entry ? "True" : "False";
  } else if (typeof entry === "number") {
    element.setAttribute("type", "i64");
    element.textContent = String(entry);
  } else if (typeof entry === "string") {
    element.setAttribute("type", "string");
    element.setAttribute("value", entry);
  } else if (Array.isArray(entry)) {
    element.setAttribute("type", "list");
    element.append(...entry.map(item => setting(document, null, item)));
  } else {
    element.setAttribute("type", "map");
    element.append(...Object.entries(entry).map(([key, item]) => setting(document, key, item)));
  }

  return element;
}

async function triggerTable(file) {
  const schema = await (await fetch("../settings-schema.json")).json();
  const scenario = await (await fetch(`/traces/${file}.json`)).json();
  const document_ = new DOMParser().parseFromString("<root/>", "text/xml");
  const cfg = scenario.settings.cfg;
  const everest = cfg.everest === true;
  const invalid = [];
  for (const key of ["start", "reset"]) {
    if (key in cfg && problem(schema, setting(document_, key, cfg[key]), everest) !== null) {
      invalid.push(key);
    }
  }

  (cfg.splits ?? []).forEach((trigger, index) => {
    if (problem(schema, setting(document_, null, trigger), everest) !== null) {
      invalid.push(`splits[${index}]`);
    }
  });
  expectEqual(`${file}: the site agrees with the module on what's invalid`, JSON.stringify(scenario.invalid), JSON.stringify(invalid));
}

async function legacyCheck(name) {
  await open(`${name}.lss`, await bytesOf(`/component/CelesteAutosplit.Tests/LegacySplits/${name}.lss`));
  expectEqual(`${name} offers the import`, false, control("legacy").hidden);
  control("import").click();
  await until(() => !control("editor").hidden || !control("status").hidden, `${name} to import`);
  expectEqual(`${name} imported`, "", control("status").hidden ? "" : control("status").textContent);
  await post(`/output/legacy/${name}`, await download());
}

async function main() {
  await until(() => frame.contentDocument?.getElementById("preset.0") !== null && frame.contentDocument?.getElementById("preset.0") !== undefined, "the page");
  win = frame.contentWindow;
  doc = frame.contentDocument;

  const realCreate = win.URL.createObjectURL.bind(win.URL);
  win.URL.createObjectURL = blob => {
    captured = blob;
    return realCreate(blob);
  };
  win.HTMLAnchorElement.prototype.click = function () {};

  await editChecks();
  await characterReferenceCheck();
  await readOnlyCheck();
  await flagChecks();
  await triggerTable("prologue.triggers");
  await triggerTable("prologue.triggers-everest");

  const legacy = new URLSearchParams(location.search).get("legacy")?.split(",") ?? [];
  for (const name of legacy) {
    await legacyCheck(name);
  }
}

main()
  .catch(error => failures.push(`the page threw: ${error.stack ?? error}`))
  .finally(() => post("/report", JSON.stringify(failures)));
