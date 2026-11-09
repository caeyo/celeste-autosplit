export function openLss(bytes) {
  const format = detectFormat(bytes);
  const text = new TextDecoder(format.encoding).decode(bytes);
  const document = new DOMParser().parseFromString(text, "text/xml");
  if (document.querySelector("parsererror") !== null) {
    throw new Error("This file isn't a valid LiveSplit XML splits file.");
  }

  if (document.documentElement.nodeName !== "Run") {
    throw new Error("This file isn't a LiveSplit splits file.");
  }

  const scan = format.width === 1 ? new TextDecoder("latin1").decode(bytes) : text;
  checkDeclaration(scan);

  const settings = [...document.documentElement.children].find(child => child.nodeName === "AutoSplitterSettings") ?? null;
  return {
    bytes,
    format,
    scan,
    document,
    settings,
    located: locateSettings(scan),
    segments: [...document.querySelectorAll("Run > Segments > Segment")]
      .map(segment => [...segment.children].find(child => child.nodeName === "Name")?.textContent.trim() ?? ""),
  };
}

function detectFormat(bytes) {
  if (bytes[0] === 0xff && bytes[1] === 0xfe) {
    return { encoding: "utf-16le", width: 2, bom: 2 };
  }

  if (bytes[0] === 0xfe && bytes[1] === 0xff) {
    return { encoding: "utf-16be", width: 2, bom: 2 };
  }

  return { encoding: "utf-8", width: 1, bom: bytes[0] === 0xef && bytes[1] === 0xbb && bytes[2] === 0xbf ? 3 : 0 };
}

function checkDeclaration(scan) {
  const declared = /^(?:\xEF\xBB\xBF)?\s*<\?xml[^>]*encoding\s*=\s*["']([^"']+)["']/i.exec(scan)?.[1].toLowerCase();
  if (declared !== undefined && declared !== "utf-8" && declared !== "utf8" && !declared.startsWith("utf-16")) {
    throw new Error(`This file says it's in the encoding ${declared}. Save it as UTF-8 and open it again.`);
  }
}

function locateSettings(text) {
  let depth = 0;
  let position = 0;
  let open = null;
  let rootClose = 0;
  while ((position = text.indexOf("<", position)) !== -1) {
    if (text.startsWith("<!--", position)) {
      position = text.indexOf("-->", position) + 3;
    } else if (text.startsWith("<![CDATA[", position)) {
      position = text.indexOf("]]>", position) + 3;
    } else if (text.startsWith("<?", position) || text.startsWith("<!", position)) {
      position = text.indexOf(">", position) + 1;
    } else if (text.startsWith("</", position)) {
      const close = text.indexOf(">", position) + 1;
      depth--;
      if (depth === 0) {
        rootClose = position;
      }

      if (open !== null && depth === 1) {
        return { start: open, end: close, inserting: false };
      }

      position = close;
    } else {
      const end = tagEnd(text, position);
      const selfClosing = text[end - 2] === "/";
      const name = /^[^\s/>]+/.exec(text.slice(position + 1, end))[0];
      if (depth === 1 && open === null && name === "AutoSplitterSettings") {
        if (selfClosing) {
          return { start: position, end, inserting: false };
        }

        open = position;
      }

      if (!selfClosing) {
        depth++;
      }

      position = end;
    }
  }

  return { start: rootClose, end: rootClose, inserting: true };
}

function tagEnd(text, position) {
  let quote = null;
  for (let i = position + 1; i < text.length; i++) {
    const c = text[i];
    if (quote !== null) {
      if (c === quote) {
        quote = null;
      }
    } else if (c === '"' || c === "'") {
      quote = c;
    } else if (c === ">") {
      return i + 1;
    }
  }

  throw new Error("This file isn't valid XML.");
}

export function isLegacy(settings) {
  return settings !== null
    && settings.querySelector("CustomSettings") === null
    && (settings.querySelector("Splits") !== null || settings.querySelector("AutoReset") !== null);
}

export function writeLss(lss, settings) {
  const { bytes, format, scan, located } = lss;
  const { start, end, inserting } = located;
  const lineBreak = /(\r?\n)([ \t]*)$/.exec(scan.slice(0, start));
  const newline = lineBreak?.[1] ?? "";
  const baseIndent = lineBreak?.[2] ?? "";
  const unit = lineBreak === null ? "" : "  ";
  const indent = inserting ? baseIndent + unit : baseIndent;

  const written = writeElement(settings, indent, newline, unit);
  const xml = inserting && lineBreak !== null ? `${indent}${written}${newline}` : written;

  const encoded = encode(xml, format);
  const offset = position => (format.width === 1 ? position : format.bom + position * 2);
  const startByte = offset(inserting && lineBreak !== null ? start - baseIndent.length : start);
  const endByte = offset(end);
  const output = new Uint8Array(startByte + encoded.length + (bytes.length - endByte));
  output.set(bytes.subarray(0, startByte), 0);
  output.set(encoded, startByte);
  output.set(bytes.subarray(endByte), startByte + encoded.length);
  return output;
}

function encode(text, format) {
  if (format.width === 1) {
    return new TextEncoder().encode(text);
  }

  const bytes = new Uint8Array(text.length * 2);
  const view = new DataView(bytes.buffer);
  for (let i = 0; i < text.length; i++) {
    view.setUint16(i * 2, text.charCodeAt(i), format.encoding === "utf-16le");
  }

  return bytes;
}

function escapeText(text) {
  return text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/\r/g, "&#xD;");
}

function escapeAttribute(text) {
  return escapeText(text).replace(/"/g, "&quot;").replace(/\t/g, "&#x9;").replace(/\n/g, "&#xA;");
}

function writeElement(element, indent, newline, unit) {
  const attributes = [...element.attributes].map(attribute => ` ${attribute.name}="${escapeAttribute(attribute.value)}"`).join("");
  const children = [...element.children];
  if (children.length > 0) {
    const childIndent = indent + unit;
    const written = children.map(child => `${newline}${childIndent}${writeElement(child, childIndent, newline, unit)}`).join("");
    return `<${element.nodeName}${attributes}>${written}${newline}${indent}</${element.nodeName}>`;
  }

  if (element.textContent.trim() !== "") {
    return `<${element.nodeName}${attributes}>${escapeText(element.textContent)}</${element.nodeName}>`;
  }

  return `<${element.nodeName}${attributes} />`;
}
