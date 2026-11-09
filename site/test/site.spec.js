import { readdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { expect, test } from "@playwright/test";

const here = import.meta.dirname;
const repo = join(here, "..", "..");
const cases = join(here, "cases");
const legacySplits = join(repo, "legacy-splits");

const control = (page, id) => page.locator(`[id="${id}"]`);

async function choosePath(page, path) {
  const chooser = page.waitForEvent("filechooser");
  await control(page, "file").click();
  await (await chooser).setFiles(path);
}

async function openEditor(page, path) {
  await page.goto("/");
  await choosePath(page, path);
  await expect(control(page, "editor")).toBeVisible();
}

async function download(page) {
  const started = page.waitForEvent("download");
  await control(page, "download").click();
  return readFileSync(await (await started).path());
}

async function chooseOption(page, id, label) {
  await control(page, id).selectOption({ label });
}

const editCases = {
  async edit(page) {
    await control(page, "setting.set_game_time").uncheck();
    await control(page, "setting.timing_method.chapter").check();
    await chooseOption(page, "reset.kind", "Exit chapter");
    await chooseOption(page, "split.0.kind", "Chapter complete");
    await chooseOption(page, "split.0.field.timing", "Chapter exit");
    await control(page, "split.0.field.chapter").fill("Core");
    await chooseOption(page, "split.1.kind", "Checkpoint");
    await control(page, "split.1.field.chapter").fill("Forsaken City");
    await chooseOption(page, "split.1.field.checkpoint", "Chasm");
    await expect(control(page, "split.2.kind")).toHaveCount(0);
    await expect(control(page, "start.field.sid")).toHaveCount(0);
    await control(page, "setting.everest").check();
    await expect(control(page, "start.field.sid")).toHaveValue("Celeste/3-CelestialResort");
  },
  async insert(page) {
    await control(page, "preset.0").click();
  },
  async compact(page) {
    await control(page, "setting.everest").check();
  },
  async utf16(page) {
    await chooseOption(page, "split.0.kind", "Enter room");
    await control(page, "split.0.field.room").fill("Café-日本");
  },
  async comment(page) {
    await control(page, "setting.high_priority").uncheck();
  },
};

for (const [name, edit] of Object.entries(editCases)) {
  test(`editing ${name}.lss gives its expected file, byte for byte`, async ({ page }) => {
    await openEditor(page, join(cases, `${name}.lss`));
    await edit(page);
    expect((await download(page)).equals(readFileSync(join(cases, `${name}.expected.lss`)))).toBe(true);
  });
}

test("an unedited download equals the file opened", async ({ page }) => {
  const path = join(cases, "edit.lss");
  await openEditor(page, path);
  expect((await download(page)).equals(readFileSync(path))).toBe(true);
});

test("choosing the same path again, after the file changed, reopens it", async ({ page }, testInfo) => {
  const path = testInfo.outputPath("changing.lss");
  writeFileSync(path, readFileSync(join(cases, "edit.lss")));
  await openEditor(page, path);

  const changed = readFileSync(join(cases, "comment.lss"));
  writeFileSync(path, changed);
  const chooser = page.waitForEvent("filechooser");
  await control(page, "file").click();
  await expect(control(page, "file")).toHaveValue("");
  await (await chooser).setFiles(path);

  await expect(control(page, "editor")).toBeVisible();
  expect((await download(page)).equals(changed)).toBe(true);
});

test("character references in kept values survive a write", async ({ page }, testInfo) => {
  const path = testInfo.outputPath("references.lss");
  writeFileSync(path, '<Run><AutoSplitterSettings><Note>a&#xD;b</Note><CustomSettings><Setting id="cfg" type="map"><Setting id="future" type="string" value="a&#xA;b&#x9;c&#xD;d" /></Setting></CustomSettings></AutoSplitterSettings></Run>');
  await openEditor(page, path);
  await control(page, "setting.everest").check();

  const output = (await download(page)).toString("utf8");
  const read = await page.evaluate(text => {
    const document = new DOMParser().parseFromString(text, "text/xml");
    return {
      attribute: document.querySelector('[id="future"]').getAttribute("value"),
      text: document.querySelector("Note").textContent,
    };
  }, output);
  expect(read).toEqual({ attribute: "a\nb\tc\rd", text: "a\rb" });
});

test("a newer schema_version is read-only", async ({ page }) => {
  await openEditor(page, join(cases, "newer.lss"));
  const warnings = control(page, "warnings");
  await expect(warnings).toContainText("newer");
  await expect(warnings).not.toContainText("isn't valid");
  const enabled = await page.locator("#editor input, #editor select, #editor button").evaluateAll(
    elements => elements.filter(element => !element.disabled).map(element => element.id),
  );
  expect(enabled).toEqual([]);
});

test("what the module reads as invalid is flagged", async ({ page }) => {
  await openEditor(page, join(cases, "flags.lss"));
  const warnings = control(page, "warnings");
  await expect(warnings).toContainText('Timing method "nonsense" isn\'t valid, so File is used.');
  await expect(warnings).toContainText("invalid or never fire");
  await expect(control(page, "start.problem")).toHaveText("This legacy split wasn't recognised, so it never fires.");
  await expect(control(page, "split.0.problem")).toHaveText("This trigger has no room, so it behaves like manual.");
  await expect(control(page, "split.1.problem")).toHaveText("This trigger has an invalid checkpoint, so it behaves like manual.");
  await expect(control(page, "split.2.problem")).toHaveText("");
  await expect(control(page, "setting.timing_method.file")).not.toBeChecked();
  await control(page, "setting.everest").check();
  await expect(control(page, "split.2.problem")).toHaveText("This trigger has an invalid chapter sid, so it behaves like manual.");
  await expect(control(page, "split.3.problem")).toHaveText("");
});

for (const file of ["prologue.triggers", "prologue.triggers-everest"]) {
  test(`the site agrees with the table the module is checked against: ${file}`, async ({ page }) => {
    const scenario = JSON.parse(readFileSync(join(repo, "traces", `${file}.json`), "utf8"));
    await page.goto("/");
    const invalid = await page.evaluate(async cfg => {
      const { problem } = await import("/settings.js");
      const schema = await (await fetch("/settings-schema.json")).json();
      const document = new DOMParser().parseFromString("<root/>", "text/xml");
      const setting = (id, entry) => {
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
          element.append(...entry.map(item => setting(null, item)));
        } else {
          element.setAttribute("type", "map");
          element.append(...Object.entries(entry).map(([key, item]) => setting(key, item)));
        }

        return element;
      };

      const everest = cfg.everest === true;
      const found = [];
      for (const key of ["start", "reset"]) {
        if (key in cfg && problem(schema, setting(key, cfg[key]), everest) !== null) {
          found.push(key);
        }
      }

      (cfg.splits ?? []).forEach((trigger, index) => {
        if (problem(schema, setting(null, trigger), everest) !== null) {
          found.push(`splits[${index}]`);
        }
      });
      return found;
    }, scenario.settings.cfg);
    expect(invalid).toEqual(scenario.invalid);
  });
}

const withoutSpaceBetweenTags = xml => xml.replace(/>\s+</g, "><").trim();

function settingsOf(text) {
  const start = text.indexOf("<AutoSplitterSettings>");
  const end = "</AutoSplitterSettings>";
  return text.slice(start, text.lastIndexOf(end) + end.length);
}

const legacyNames = readdirSync(legacySplits)
  .filter(file => file.endsWith(".lss"))
  .map(file => file.slice(0, -".lss".length));

test("the legacy files are there", () => {
  expect(legacyNames.length).toBeGreaterThan(0);
});

for (const name of legacyNames) {
  test(`importing ${name}.lss gives its expected settings`, async ({ page }) => {
    await page.goto("/");
    await choosePath(page, join(legacySplits, `${name}.lss`));
    await expect(control(page, "legacy")).toBeVisible();
    await control(page, "import").click();
    await expect(control(page, "editor")).toBeVisible();

    const output = settingsOf((await download(page)).toString("utf8"));
    const expected = readFileSync(join(legacySplits, `${name}.expected.xml`), "utf8");
    expect(withoutSpaceBetweenTags(output)).toBe(withoutSpaceBetweenTags(expected));
  });
}
