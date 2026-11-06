use std::{env, fmt::Write, fs, path::PathBuf};

use serde_json::{Map, Value};

const SCHEMA: &str = "../schema/settings-schema.json";

fn main() {
    println!("cargo::rerun-if-changed={SCHEMA}");
    let text = fs::read_to_string(SCHEMA).expect("reading the schema");
    let schema: Value = serde_json::from_str(&text).expect("parsing the schema");
    let mut out = String::new();
    let version = int(&schema["schema_version"]);
    writeln!(out, "pub const SCHEMA_VERSION: i64 = {version};").unwrap();
    settings(&mut out, &schema["settings"]);
    fields(&mut out, object(&schema["fields"]));
    triggers(&mut out, &schema["triggers"]);
    chapters(&mut out, &schema["chapters"]);
    let path = PathBuf::from(env::var_os("OUT_DIR").unwrap()).join("schema.rs");
    fs::write(path, out).expect("writing schema.rs");
}

fn settings(out: &mut String, settings: &Value) {
    for setting in array(settings) {
        if setting.get("host").is_some() {
            continue;
        }
        let key = string(&setting["key"]);
        let default = &setting["default"];
        match string(&setting["type"]) {
            "bool" => {
                let default = default.as_bool().expect("a bool default");
                writeln!(out, "pub const {}: bool = {default};", upper(key)).unwrap();
            }
            "string" => enumeration(
                out,
                &camel(key),
                &names(&setting["values"]),
                string(default),
            ),
            "trigger" => {
                let default = object(default);
                assert_eq!(default.len(), 1, "{key}'s default has fields");
                let kind = camel(string(&default["kind"]));
                writeln!(out, "pub const {}: Kind = Kind::{kind};", upper(key)).unwrap();
            }
            "list" => {
                assert_eq!(string(&setting["items"]), "trigger");
                assert!(array(default).is_empty(), "{key}'s default isn't empty");
            }
            other => panic!("{key} has the type {other}"),
        }
    }
}

fn fields(out: &mut String, fields: &Map<String, Value>) {
    let mut names = Vec::new();
    let mut everest = Vec::new();
    let mut replaces = Vec::new();
    for (name, field) in fields {
        names.push(name.as_str());
        let values = field.get("values");
        match (string(&field["type"]), values, field.get("values_from")) {
            ("i64", Some(values), None) => {
                let values = array(values).iter().map(|value| int(&value["value"]));
                let values = values.map(|value| value.to_string()).collect::<Vec<_>>();
                let values = values.join(", ");
                writeln!(
                    out,
                    "pub const {}_VALUES: &[i64] = &[{values}];",
                    upper(name)
                )
                .unwrap();
            }
            ("string", Some(values), None) => {
                let default = string(field.get("default").expect("a default"));
                enumeration(out, &camel(name), &self::names(values), default);
            }
            ("i64", None, Some(_)) | ("string", None, None) => {
                assert!(field.get("default").is_none(), "{name} has a default");
            }
            (other, _, _) => panic!("{name} has the type {other}, or both values and values_from"),
        }
        if let Some(no_chapter) = field.get("no_chapter") {
            let value = int(&no_chapter["value"]);
            writeln!(out, "pub const NO_{}: i64 = {value};", upper(name)).unwrap();
        }
        if field.get("everest").and_then(Value::as_bool) == Some(true) {
            everest.push(name.as_str());
        }
        if let Some(replaced) = field.get("replaces") {
            replaces.push((name.as_str(), string(replaced)));
        }
    }
    variants(out, "Field", &names);
    out.push_str("impl Field {\n");
    names_match(out, &names);
    let everest = any_of(&everest);
    writeln!(
        out,
        "    pub fn everest(self) -> bool {{\n        {everest}\n    }}"
    )
    .unwrap();
    out.push_str("    pub fn replaces(self) -> Option<Self> {\n        match self {\n");
    for (name, replaced) in replaces {
        let (name, replaced) = (camel(name), camel(replaced));
        writeln!(out, "            Self::{name} => Some(Self::{replaced}),").unwrap();
    }
    out.push_str("            _ => None,\n        }\n    }\n}\n");
}

fn triggers(out: &mut String, triggers: &Value) {
    let triggers = array(triggers);
    let kinds = triggers.iter().map(|trigger| string(&trigger["kind"]));
    let kinds = kinds.collect::<Vec<_>>();
    variants(out, "Kind", &kinds);
    out.push_str("impl Kind {\n");
    from_name(out, &kinds);
    names_match(out, &kinds);
    for list in ["required", "optional"] {
        writeln!(
            out,
            "    pub fn {list}(self) -> &'static [Field] {{\n        match self {{"
        )
        .unwrap();
        for trigger in triggers {
            let fields = array(&trigger[list]).iter().map(|field| {
                let field = string(field);
                format!("Field::{}", camel(field))
            });
            let fields = fields.collect::<Vec<_>>().join(", ");
            let kind = camel(string(&trigger["kind"]));
            writeln!(out, "            Self::{kind} => &[{fields}],").unwrap();
        }
        out.push_str("        }\n    }\n");
    }
    let allowed = triggers
        .iter()
        .filter(|trigger| trigger.get("allow_no_chapter").and_then(Value::as_bool) == Some(true))
        .map(|trigger| string(&trigger["kind"]))
        .collect::<Vec<_>>();
    let allowed = any_of(&allowed);
    writeln!(
        out,
        "    pub fn allow_no_chapter(self) -> bool {{\n        {allowed}\n    }}\n}}"
    )
    .unwrap();
}

fn chapters(out: &mut String, chapters: &Value) {
    out.push_str("pub struct Chapter {\n    pub id: i64,\n");
    out.push_str("    pub checkpoints: &'static [&'static [&'static str]],\n}\n");
    out.push_str("pub const CHAPTERS: &[Chapter] = &[\n");
    for chapter in array(chapters) {
        let checkpoints = array(&chapter["checkpoints"]).iter().map(|checkpoint| {
            let rooms = array(&checkpoint["rooms"])
                .iter()
                .map(|room| format!("{:?}", string(room)));
            format!("&[{}]", rooms.collect::<Vec<_>>().join(", "))
        });
        let checkpoints = checkpoints.collect::<Vec<_>>().join(", ");
        let id = int(&chapter["id"]);
        writeln!(
            out,
            "    Chapter {{ id: {id}, checkpoints: &[{checkpoints}] }},"
        )
        .unwrap();
    }
    out.push_str("];\n");
}

fn enumeration(out: &mut String, name: &str, values: &[&str], default: &str) {
    assert!(
        values.contains(&default),
        "{name}'s default {default} isn't allowed"
    );
    writeln!(
        out,
        "#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]\npub enum {name} {{"
    )
    .unwrap();
    for value in values {
        if *value == default {
            out.push_str("    #[default]\n");
        }
        writeln!(out, "    {},", camel(value)).unwrap();
    }
    writeln!(out, "}}\nimpl {name} {{").unwrap();
    from_name(out, values);
    names_match(out, values);
    out.push_str("}\n");
}

fn variants(out: &mut String, name: &str, values: &[&str]) {
    writeln!(
        out,
        "#[derive(Clone, Copy, Debug, PartialEq, Eq)]\npub enum {name} {{"
    )
    .unwrap();
    for value in values {
        writeln!(out, "    {},", camel(value)).unwrap();
    }
    out.push_str("}\n");
}

fn from_name(out: &mut String, values: &[&str]) {
    out.push_str("    pub fn from_name(name: &str) -> Option<Self> {\n        match name {\n");
    for value in values {
        writeln!(
            out,
            "            {value:?} => Some(Self::{}),",
            camel(value)
        )
        .unwrap();
    }
    out.push_str("            _ => None,\n        }\n    }\n");
}

fn names_match(out: &mut String, values: &[&str]) {
    out.push_str("    pub fn name(self) -> &'static str {\n        match self {\n");
    for value in values {
        writeln!(out, "            Self::{} => {value:?},", camel(value)).unwrap();
    }
    out.push_str("        }\n    }\n");
}

fn any_of(values: &[&str]) -> String {
    if values.is_empty() {
        return "false".to_owned();
    }
    let values = values.iter().map(|value| format!("Self::{}", camel(value)));
    format!("matches!(self, {})", values.collect::<Vec<_>>().join(" | "))
}

fn names(values: &Value) -> Vec<&str> {
    array(values)
        .iter()
        .map(|value| string(&value["value"]))
        .collect()
}

fn camel(name: &str) -> String {
    name.split('_')
        .map(|word| {
            let mut chars = word.chars();
            chars.next().map_or_else(String::new, |first| {
                first.to_ascii_uppercase().to_string() + chars.as_str()
            })
        })
        .collect()
}

fn upper(name: &str) -> String {
    name.to_ascii_uppercase()
}

fn string(value: &Value) -> &str {
    value
        .as_str()
        .unwrap_or_else(|| panic!("expected a string, found {value}"))
}

fn int(value: &Value) -> i64 {
    value
        .as_i64()
        .unwrap_or_else(|| panic!("expected an integer, found {value}"))
}

fn array(value: &Value) -> &Vec<Value> {
    value
        .as_array()
        .unwrap_or_else(|| panic!("expected an array, found {value}"))
}

fn object(value: &Value) -> &Map<String, Value> {
    value
        .as_object()
        .unwrap_or_else(|| panic!("expected an object, found {value}"))
}
