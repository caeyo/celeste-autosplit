export const find = (map, key) => (map ? [...map.children].find(element => element.getAttribute("id") === key) ?? null : null);

export const type = element => element?.getAttribute("type") ?? null;

export const items = list => (list ? [...list.children] : []);

export function findMap(map, key) {
  const element = find(map, key);
  return type(element) === "map" ? element : null;
}

export function value(element) {
  switch (type(element)) {
    case "bool": {
      const text = element.textContent.trim().toLowerCase();
      return text === "true" ? true : text === "false" ? false : null;
    }
    case "i64": {
      const text = element.textContent.trim();
      if (!/^[+-]?\d+$/.test(text)) {
        return null;
      }

      const number = BigInt(text);
      return number >= -(2n ** 63n) && number < 2n ** 63n ? Number(number) : null;
    }
    case "string":
      return element.getAttribute("value") ?? "";
    default:
      return null;
  }
}

export function rawText(element) {
  if (element === null) {
    return "";
  }

  const text = element.getAttribute("value");
  return text !== null && text.length > 0 ? text : element.textContent;
}

export function remove(map, key) {
  const element = find(map, key);
  if (element !== null) {
    map.removeChild(element);
  }
}

export function set(map, key, newValue, order) {
  if (newValue === null) {
    remove(map, key);
    return;
  }

  let element = find(map, key);
  if (element === null) {
    element = map.ownerDocument.createElement("Setting");
    element.setAttribute("id", key);
    insertOrdered(map, element, key, order);
  }

  assign(element, newValue);
}

export function setContainer(map, key, containerType, order) {
  let element = find(map, key);
  if (element === null) {
    element = map.ownerDocument.createElement("Setting");
    element.setAttribute("id", key);
    insertOrdered(map, element, key, order);
  } else if (type(element) === containerType) {
    return element;
  }

  makeContainer(element, containerType);
  return element;
}

export function makeContainer(element, containerType) {
  clear(element);
  element.setAttribute("type", containerType);
}

export function newItem(document) {
  const element = document.createElement("Setting");
  element.setAttribute("type", "map");
  return element;
}

function clear(element) {
  const id = element.getAttribute("id");
  for (const attribute of [...element.attributes]) {
    element.removeAttribute(attribute.name);
  }

  while (element.firstChild !== null) {
    element.removeChild(element.firstChild);
  }

  if (id !== null && id.length > 0) {
    element.setAttribute("id", id);
  }
}

function insertOrdered(map, element, key, order) {
  const rank = order.indexOf(key);
  let after = null;
  for (const sibling of map.children) {
    const siblingRank = order.indexOf(sibling.getAttribute("id"));
    if (siblingRank >= 0 && siblingRank < rank) {
      after = sibling;
    }
  }

  if (after !== null) {
    map.insertBefore(element, after.nextSibling);
  } else {
    map.insertBefore(element, map.firstChild);
  }
}

function assign(element, newValue) {
  clear(element);
  if (typeof newValue === "boolean") {
    element.setAttribute("type", "bool");
    element.textContent = newValue ? "True" : "False";
  } else if (typeof newValue === "number") {
    element.setAttribute("type", "i64");
    element.textContent = String(newValue);
  } else if (typeof newValue === "string") {
    element.setAttribute("type", "string");
    element.setAttribute("value", newValue);
  } else {
    throw new Error(`Can't write a setting of type ${typeof newValue}`);
  }
}

export function fieldsOf(schema, trigger) {
  return [...trigger.required, ...trigger.optional].map(name => ({ name, ...schema.fields[name] }));
}

export function triggerOfKind(schema, kind) {
  return schema.triggers.find(trigger => trigger.kind === kind) ?? null;
}

const FIELD_WORDS = { timing: "split timing", sid: "chapter SID" };

const fieldWord = (schema, name) => FIELD_WORDS[name] ?? schema.fields[name].label.toLowerCase();

export function problem(schema, trigger, everest) {
  if (type(trigger) !== "map") {
    return "isn't a map";
  }

  const kindName = value(find(trigger, "kind"));
  if (typeof kindName !== "string") {
    return "has no kind";
  }

  const kind = triggerOfKind(schema, kindName);
  if (kind === null) {
    return `has the unknown kind "${kindName}"`;
  }

  const present = new Set();
  let chapter = null;
  let checkpoint = null;
  for (const field of fieldsOf(schema, kind)) {
    if (field.everest && !everest) {
      continue;
    }

    const element = find(trigger, field.name);
    if (element === null) {
      continue;
    }

    const fieldValue = value(element);
    const invalid = `has an invalid ${fieldWord(schema, field.name)}`;
    if (type(element) !== field.type || fieldValue === null) {
      return invalid;
    }

    if (field.values && !field.values.some(choice => choice.value === fieldValue)) {
      return invalid;
    }

    if (field.values_from === "chapters") {
      if (fieldValue < 0) {
        return invalid;
      }

      chapter = fieldValue;
    } else if (field.values_from === "checkpoints") {
      checkpoint = fieldValue;
    }

    present.add(field.name);
    if (field.replaces) {
      present.add(field.replaces);
    }
  }

  const missing = kind.required.find(name => !present.has(name));
  if (missing !== undefined) {
    return `has no ${fieldWord(schema, missing)}`;
  }

  if (checkpoint !== null) {
    const known = schema.chapters.find(entry => entry.id === chapter);
    if (known === undefined || checkpoint < 1 || checkpoint > known.checkpoints.length) {
      return "has an invalid checkpoint";
    }
  }

  return null;
}
