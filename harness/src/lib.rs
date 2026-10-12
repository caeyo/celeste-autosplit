//! What the harness and `fake-celeste` share: the formats of the layout files in `layouts/`,
//! which `layouts/README.md` describes.

use std::{collections::BTreeMap, fs, path::Path};

use anyhow::Context;
use serde::{Deserialize, de::DeserializeOwned};

/// `<flavour>.json`: a vanilla flavour's layout, as the layout probe measured it.
#[derive(Deserialize)]
pub struct Flavour {
    pub flavour: String,
    pub os: String,
    pub pointer_size: u64,
    pub string: StringLayout,
    pub roots: BTreeMap<String, Root>,
}

/// Where a string object holds its length (an `i32`) and its UTF-16 characters.
#[derive(Deserialize)]
pub struct StringLayout {
    pub length: u64,
    pub chars: u64,
}

/// A static field that the auto splitter finds through machine code.
#[derive(Deserialize)]
pub struct Root {
    /// Hex bytes, with `??` for any byte.
    pub signature: String,
    /// Patterns for the store into the slot after the signature, with `SS` for the slot's
    /// address, 4 or 8 bytes in a row.
    pub store: Vec<String>,
    pub paths: BTreeMap<String, FieldPath>,
}

/// Instance fields read from the root's object: each one's offset and .NET type, one per hop.
#[derive(Deserialize)]
pub struct FieldPath {
    pub offsets: Vec<u64>,
    pub types: Vec<String>,
}

/// `everest.json`: Everest's own info struct.
#[derive(Deserialize)]
pub struct Everest {
    pub pointer_size: u64,
    pub magic: String,
    pub info_version: u8,
    pub fields: BTreeMap<String, EverestField>,
}

#[derive(Deserialize)]
pub struct EverestField {
    pub offset: u64,
    #[serde(rename = "type")]
    pub ty: String,
    /// Each flag's bit number, for flag fields.
    #[serde(default)]
    pub flags: BTreeMap<String, u32>,
}

/// The name of Everest's layout file. Every other file is a vanilla flavour's.
pub const EVEREST_FILE: &str = "everest.json";

pub fn read<T: DeserializeOwned>(file: &Path) -> anyhow::Result<T> {
    let text = fs::read_to_string(file).with_context(|| format!("reading {}", file.display()))?;
    serde_json::from_str(&text).with_context(|| format!("parsing {}", file.display()))
}

/// How many bytes of a store pattern are the slot's address.
pub fn slot_width(pattern: &str) -> usize {
    pattern.split(' ').filter(|&byte| byte == "SS").count()
}
