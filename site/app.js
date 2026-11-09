import { isLegacy, openLss, writeLss } from "./lss.js";
import {
  fieldsOf,
  find,
  findMap,
  items,
  makeContainer,
  newItem,
  problem,
  rawText,
  remove,
  set,
  setContainer,
  triggerOfKind,
  type,
  value,
} from "./settings.js";

const FLAGS_FIRST = ["set_game_time", "high_priority", "everest"];
const TRIGGERS_FIRST = ["reset", "start"];
const TOP_LEVEL_ORDER = ["schema_version", "cfg"];

const schema = await (await fetch("settings-schema.json")).json();
const cfgOrder = schema.settings.map(setting => setting.key);
const splitsSetting = schema.settings.find(setting => setting.type === "list");
const everestSetting = schema.settings.find(setting => setting.key === "everest");

const page = id => document.getElementById(id);
const settingInputs = new Map();

let lss = null;
let fileName = "";
let settingsElement = null;
let map = null;
let cfg = null;
let readOnly = false;
let dirty = false;
let rows = [];

function tag(name, properties = {}, ...children) {
  const element = document.createElement(name);
  Object.assign(element, properties);
  element.append(...children);
  return element;
}

const orderedBy = (settings, first) => {
  const rank = setting => {
    const index = first.indexOf(setting.key);
    return index >= 0 ? index : first.length;
  };
  return [...settings].sort((a, b) => rank(a) - rank(b));
};

const keep = Symbol("keep");
const anyLabel = field => `Any ${field.label.toLowerCase()}`;

function buildStatic() {
  page("chapters").replaceChildren(...schema.chapters.map(chapter => tag("option", { value: chapter.name })));

  const flags = page("flags");
  for (const setting of orderedBy(schema.settings.filter(setting => setting.type === "bool"), FLAGS_FIRST)) {
    const box = tag("input", { type: "checkbox", title: setting.description ?? "", id: `setting.${setting.key}` });
    box.addEventListener("change", () => settingChanged(setting, box.checked));
    settingInputs.set(setting, box);
    const label = tag("label", { title: setting.description ?? "" }, box, ` ${setting.label}`);
    if (setting.host === "livesplit") {
      label.append(" ", tag("span", { className: "host-only" }, "LiveSplit (Windows) only"));
    }

    flags.append(label);
  }

  const presets = page("presets");
  presets.append(tag("span", {}, "Apply preset"));
  schema.presets.forEach((preset, index) => {
    const button = tag("button", { type: "button", id: `preset.${index}`, className: "preset" }, preset.label);
    button.addEventListener("click", () => applyPreset(preset));
    presets.append(button);
  });

  const settings = page("settings");
  for (const setting of schema.settings.filter(setting => setting.type === "string" && setting.values)) {
    const group = tag("div", { className: "row" }, tag("span", {}, setting.label));
    const radios = setting.values.map(choice => {
      const radio = tag("input", { type: "radio", name: setting.key, id: `setting.${setting.key}.${choice.value}` });
      radio.addEventListener("change", () => {
        if (radio.checked) {
          settingChanged(setting, choice.value);
        }
      });
      group.append(tag("label", {}, radio, ` ${choice.label}`));
      return radio;
    });
    settingInputs.set(setting, radios);
    settings.append(group);
  }

  page("file").addEventListener("click", () => {
    page("file").value = "";
  });
  page("file").addEventListener("change", openFile);
  page("import").addEventListener("click", importLegacy);
  page("download").addEventListener("click", download);
}

async function openFile() {
  const file = page("file").files[0];
  if (file === undefined) {
    return;
  }

  fileName = file.name;
  showStatus(null);
  page("editor").hidden = true;
  page("download").hidden = true;
  page("legacy").hidden = true;
  try {
    lss = openLss(new Uint8Array(await file.arrayBuffer()));
    if (isLegacy(lss.settings)) {
      settingsElement = lss.settings;
      page("legacy").hidden = false;
    } else {
      startEditing(lss.settings);
    }
  } catch (error) {
    lss = null;
    showStatus(error.message);
  }
}

function showStatus(message) {
  const status = page("status");
  status.textContent = message ?? "";
  status.hidden = message === null;
}

async function importLegacy() {
  const button = page("import");
  button.disabled = true;
  showStatus(null);
  try {
    const { migrate } = await import("./legacy.js");
    const xml = new XMLSerializer().serializeToString(settingsElement);
    const migrated = await migrate(xml);
    const document = new DOMParser().parseFromString(migrated, "text/xml");
    page("legacy").hidden = true;
    startEditing(document.documentElement, true);
  } catch (error) {
    showStatus(`The import failed: ${error.message}`);
  } finally {
    button.disabled = false;
  }
}

function startEditing(settings, alreadyDirty = false) {
  dirty = alreadyDirty;
  settingsElement = settings ?? lss.document.createElement("AutoSplitterSettings");
  let customSettings = [...settingsElement.children].find(child => child.nodeName === "CustomSettings") ?? null;
  if (customSettings === null) {
    const document = settingsElement.ownerDocument;
    if ([...settingsElement.children].every(child => child.nodeName !== "Version")) {
      const version = document.createElement("Version");
      version.textContent = "1.0";
      settingsElement.insertBefore(version, settingsElement.firstChild);
    }

    customSettings = document.createElement("CustomSettings");
    settingsElement.append(customSettings);
  }

  map = customSettings;
  cfg = findMap(map, "cfg");
  const version = value(find(map, "schema_version"));
  readOnly = typeof version === "number" && version > schema.schema_version;
  rows = [];
  page("triggers").replaceChildren();
  page("segments").replaceChildren();
  for (const setting of orderedBy(schema.settings.filter(setting => setting.type === "trigger"), TRIGGERS_FIRST)) {
    const row = newRow(setting.key, setting.label, setting.description, setting, 0);
    rows.push(row);
    page("triggers").append(tag("hr"), row.panel);
  }

  lss.segments.forEach((name, index) => {
    const row = newRow(`split.${index}`, name || `Segment ${index + 1}`, name, splitsSetting, index);
    rows.push(row);
    page("segments").append(row.panel);
  });

  renderAll();
  page("editor").hidden = false;
  page("download").hidden = false;
}

function newRow(id, label, description, setting, index) {
  const row = {
    id,
    setting,
    index,
    isSplit: setting.type === "list",
    inputs: [],
    kind: tag("select", { className: "kind", id: `${id}.kind`, title: "Kind" }),
    fields: tag("div", { className: "trigger-fields", hidden: true }),
    problem: tag("div", { className: "problem", id: `${id}.problem`, hidden: true }),
  };
  row.panel = tag(
    "div",
    { className: "trigger" },
    tag("div", { className: "trigger-head" }, tag("span", { className: "trigger-name", title: description ?? label }, label), row.kind),
    row.fields,
    row.problem,
  );
  row.kind.addEventListener("change", () => kindChanged(row));
  return row;
}

function everest() {
  const stored = value(find(cfg, everestSetting.key));
  return typeof stored === "boolean" ? stored : everestSetting.default === true;
}

function splitsList() {
  const list = find(cfg, splitsSetting.key);
  return type(list) === "list" ? list : null;
}

function triggerOf(row) {
  if (row.isSplit) {
    return items(splitsList())[row.index] ?? null;
  }

  return find(cfg, row.setting.key);
}

const kindName = trigger => {
  const name = value(find(trigger, "kind"));
  return typeof name === "string" ? name : null;
};

function defaultKind(setting) {
  return setting.default && setting.default.kind ? setting.default.kind : "manual";
}

function renderAll() {
  page("presets").querySelectorAll("button").forEach(button => {
    button.disabled = readOnly;
  });
  page("download").disabled = readOnly;
  renderSettings();
  for (const row of rows) {
    renderRow(row, null);
  }

  renderWarnings();
}

function renderSettings() {
  for (const [setting, input] of settingInputs) {
    const element = find(cfg, setting.key);
    const stored = value(element);
    if (setting.type === "bool") {
      input.disabled = readOnly;
      input.checked = typeof stored === "boolean" ? stored : setting.default === true;
    } else {
      const current = element === null ? setting.default : stored;
      input.forEach((radio, index) => {
        radio.disabled = readOnly;
        radio.checked = setting.values[index].value === current;
      });
    }
  }
}

function rowProblem(trigger) {
  if (trigger === null || readOnly) {
    return null;
  }

  const invalid = problem(schema, trigger, everest());
  if (invalid !== null) {
    return `This trigger ${invalid}, so it behaves like manual.`;
  }

  if (kindName(trigger) === "unknown") {
    return "This legacy split wasn't recognised, so it never fires.";
  }

  return null;
}

function renderWarnings() {
  const lines = [];
  if (readOnly) {
    lines.push("This split file's settings use a newer version of the auto splitter that this page has not yet been updated to handle. This page will be updated soon. In the mean time, the settings are read-only here.");
  } else {
    for (const setting of settingInputs.keys()) {
      if (!setting.values) {
        continue;
      }

      const element = find(cfg, setting.key);
      if (element !== null && !setting.values.some(choice => choice.value === value(element))) {
        const fallback = setting.values.find(choice => choice.value === setting.default).label;
        lines.push(`${setting.label} "${rawText(element)}" isn't valid, so ${fallback} is used.`);
      }
    }

    if (rows.some(row => rowProblem(triggerOf(row)) !== null)) {
      lines.push("Some triggers below are invalid or never fire. Each one says why.");
    }
  }

  const warnings = page("warnings");
  warnings.textContent = lines.join("\n");
  warnings.style.whiteSpace = "pre-line";
  warnings.hidden = lines.length === 0;
}

function renderRow(row, except) {
  const trigger = triggerOf(row);
  const name = trigger === null ? defaultKind(row.setting) : kindName(trigger);
  row.kind.disabled = readOnly;
  renderKind(row, trigger, name);

  const known = triggerOfKind(schema, name);
  const shown = known === null ? [] : fieldsOf(schema, known).filter(field => !field.everest || everest());
  if (row.inputs.map(input => input.field.name).join() !== shown.map(field => field.name).join()) {
    row.inputs = shown.map(field => newFieldInput(row, field, known));
    row.fields.replaceChildren(...row.inputs.map(input => input.input));
    row.fields.hidden = shown.length === 0;
  }

  for (const input of row.inputs) {
    if (input.input !== except) {
      renderField(input, known, trigger);
    }

    input.input.disabled = readOnly || isReplaced(input.field, known, trigger);
  }

  const message = rowProblem(trigger);
  row.problem.textContent = message ?? "";
  row.problem.hidden = message === null;
}

function newFieldInput(row, field, known) {
  const id = `${row.id}.field.${field.name}`;
  const title = field.description ?? field.label;
  let input;
  if (field.read_only) {
    input = tag("span", { id, title });
  } else if (field.values_from === "chapters") {
    input = tag("input", { type: "text", id, title, placeholder: known.required.includes(field.name) ? field.label : anyLabel(field) });
    input.setAttribute("list", "chapters");
    input.addEventListener("input", () => chapterChanged(row, field, input));
  } else if (field.values || field.values_from) {
    input = tag("select", { id, title });
    input.addEventListener("change", () => selectChanged(row, field, input));
  } else {
    input = tag("input", { type: "text", id, title, placeholder: field.label });
    input.addEventListener("input", () => fieldChanged(row, field, input, input.value.length === 0 ? null : input.value));
  }

  return { field, input };
}

function isReplaced(field, known, trigger) {
  return Object.entries(schema.fields).some(([name, other]) => other.replaces === field.name
    && known !== null
    && fieldsOf(schema, known).some(entry => entry.name === name)
    && (!other.everest || everest())
    && find(trigger, name) !== null);
}

function renderKind(row, trigger, name) {
  const context = row.setting.key;
  const choices = schema.triggers
    .filter(entry => entry.offer.includes(context) || entry.kind === name)
    .map(entry => ({ label: entry.label, value: entry.kind }));
  if (triggerOfKind(schema, name) === null) {
    choices.push({ label: name !== null ? `Unknown: ${name}` : "(invalid)", value: name ?? keep });
  }

  fill(row.kind, choices, choices.findIndex(choice => choice.value === (name ?? keep)));
}

function fill(select, choices, selected) {
  select.choices = choices;
  select.replaceChildren(...choices.map(choice => tag("option", {}, choice.label)));
  select.selectedIndex = selected;
}

function renderField(input, known, trigger) {
  const element = find(trigger, input.field.name);
  const stored = value(element);
  if (input.field.read_only) {
    input.input.textContent = rawText(element);
  } else if (input.field.values_from === "chapters") {
    const wanted = element === null ? "" : typeof stored === "number" ? (schema.chapters.find(chapter => chapter.id === stored)?.name ?? String(stored)) : rawText(element);
    if (input.input.value !== wanted) {
      input.input.value = wanted;
    }
  } else if (input.input.tagName === "SELECT") {
    renderChoices(input.input, input.field, known, trigger, element, stored);
  } else {
    const wanted = typeof stored === "string" ? stored : "";
    if (input.input.value !== wanted) {
      input.input.value = wanted;
    }
  }
}

function renderChoices(select, field, known, trigger, element, stored) {
  const choices = [];
  if (!known.required.includes(field.name) && field.default === undefined) {
    choices.push({ label: anyLabel(field), value: null });
  }

  if (field.values) {
    choices.push(...field.values.map(entry => ({ label: entry.label, value: entry.value })));
  } else if (field.values_from === "checkpoints") {
    choices.push(...checkpointChoices(trigger));
  }

  const current = element === null ? field.default ?? null : stored;
  let selected = choices.findIndex(choice => choice.value === current);
  if (selected < 0 && element !== null) {
    choices.push({ label: `${rawText(element)} (invalid)`, value: keep });
    selected = choices.length - 1;
  }

  fill(select, choices, selected);
}

function checkpointChoices(trigger) {
  const chapterId = value(find(trigger, "chapter"));
  const side = value(find(trigger, "side"));
  const chapter = schema.chapters.find(entry => entry.id === chapterId);
  if (chapter === undefined) {
    return [];
  }

  const sideIndex = typeof side === "number" ? side : 0;
  return chapter.checkpoints.map((checkpoint, index) => ({ label: checkpoint.names[sideIndex] ?? `Checkpoint ${index + 1}`, value: index + 1 }));
}

function triggerOrder(known) {
  return ["kind", ...known.required, ...known.optional];
}

function applyPreset(preset) {
  ensureCfg();
  for (const setting of schema.settings) {
    if (!(setting.key in preset.settings)) {
      continue;
    }

    const presetValue = preset.settings[setting.key];
    if (setting.type === "trigger") {
      applyTrigger(rows.find(row => row.setting === setting), presetValue);
    } else {
      set(cfg, setting.key, presetValue, cfgOrder);
    }
  }

  for (const row of rows) {
    applyTriggerFields(preset, triggerOf(row));
  }

  commit();
  renderAll();
}

function applyTrigger(row, expected) {
  const trigger = ensureTrigger(row);
  const known = triggerOfKind(schema, expected.kind);
  const order = triggerOrder(known);
  for (const name of Object.keys(schema.fields)) {
    if (!(name in expected)) {
      remove(trigger, name);
    }
  }

  for (const [key, entry] of Object.entries(expected)) {
    set(trigger, key, entry, order);
  }
}

function applyTriggerFields(preset, trigger) {
  const known = trigger === null ? null : triggerOfKind(schema, kindName(trigger));
  const values = known === null ? undefined : preset.trigger_fields[known.kind];
  if (values === undefined) {
    return;
  }

  const order = triggerOrder(known);
  for (const [key, entry] of Object.entries(values)) {
    set(trigger, key, entry, order);
  }
}

function settingChanged(setting, newValue) {
  ensureCfg();
  set(cfg, setting.key, newValue, cfgOrder);
  commit();
  if (setting === everestSetting) {
    rows.forEach(row => renderRow(row, null));
    renderWarnings();
  }
}

function selectChanged(row, field, select) {
  const choice = select.choices[select.selectedIndex];
  if (choice === undefined || choice.value === keep) {
    return;
  }

  fieldChanged(row, field, null, choice.value);
}

function chapterChanged(row, field, input) {
  const text = input.value.trim();
  const named = schema.chapters.find(chapter => chapter.name.toLowerCase() === text.toLowerCase());
  let parsed;
  if (text.length === 0) {
    parsed = null;
  } else if (named !== undefined) {
    parsed = named.id;
  } else if (/^\d+$/.test(text) && Number.isSafeInteger(Number(text))) {
    parsed = Number(text);
  } else {
    row.problem.textContent = `"${input.value}" isn't a chapter.`;
    row.problem.hidden = false;
    return;
  }

  fieldChanged(row, field, input, parsed);
}

function fieldChanged(row, field, source, newValue) {
  const existing = find(triggerOf(row), field.name);
  const before = existing === null ? null : value(existing);
  if (before === newValue && (existing === null || before !== null)) {
    renderRow(row, source);
    return;
  }

  const trigger = ensureTrigger(row);
  set(trigger, field.name, newValue, triggerOrder(triggerOfKind(schema, kindName(trigger))));
  commit();
  renderRow(row, source);
}

function kindChanged(row) {
  const choice = row.kind.choices[row.kind.selectedIndex];
  if (choice === undefined || choice.value === keep) {
    return;
  }

  const before = triggerOf(row);
  const newKind = choice.value;
  if ((before === null ? defaultKind(row.setting) : kindName(before)) === newKind) {
    return;
  }

  const trigger = ensureTrigger(row);
  const known = triggerOfKind(schema, newKind);
  const taken = new Set(fieldsOf(schema, known).map(field => field.name));
  for (const name of Object.keys(schema.fields)) {
    if (!taken.has(name)) {
      remove(trigger, name);
    }
  }

  set(trigger, "kind", newKind, triggerOrder(known));
  commit();
  renderRow(row, null);
}

function ensureCfg() {
  cfg ??= setContainer(map, "cfg", "map", TOP_LEVEL_ORDER);
}

function ensureTrigger(row) {
  ensureCfg();
  let trigger = triggerOf(row);
  if (trigger === null) {
    if (row.isSplit) {
      const list = ensureSplits(row.index + 1);
      trigger = items(list)[row.index];
    } else {
      trigger = setContainer(cfg, row.setting.key, "map", cfgOrder);
    }
  } else if (type(trigger) !== "map") {
    makeContainer(trigger, "map");
  }

  if (kindName(trigger) === null) {
    set(trigger, "kind", defaultKind(row.setting), ["kind"]);
  }

  return trigger;
}

function ensureSplits(count) {
  const list = setContainer(cfg, splitsSetting.key, "list", cfgOrder);
  while (items(list).length < count) {
    const item = newItem(list.ownerDocument);
    set(item, "kind", "manual", ["kind"]);
    list.append(item);
  }

  return list;
}

function commit() {
  if (readOnly) {
    return;
  }

  if (find(map, "schema_version") === null) {
    set(map, "schema_version", schema.schema_version, TOP_LEVEL_ORDER);
  }

  dirty = true;
  renderWarnings();
}

function download() {
  const bytes = dirty ? writeLss(lss, settingsElement) : lss.bytes;
  const url = URL.createObjectURL(new Blob([bytes], { type: "application/octet-stream" }));
  const link = tag("a", { href: url, download: fileName });
  link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

buildStatic();
