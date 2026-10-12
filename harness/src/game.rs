//! The fake game: the memory that a layout in `layouts/` describes, built in the harness's own
//! process for the auto splitter to read, and where each value the reading test sets goes.
//!
//! For a vanilla flavour's layout, it builds:
//! - for each root, code holding the root's signature, then a store into the root's static
//!   slot, using the store pattern whose address has the width asked for (by default, the first
//!   one). The slot holds the root's object.
//! - every object along each path from the root's object, with "Celeste" in `Title`.
//!
//! The code starts with a decoy: the same code, storing into a slot whose object has another
//! title.
//!
//! For Everest's, it builds the info struct, at the start of a mapping of its own.
//!
//! What the auto splitter reads through 32-bit addresses is mapped below 4 GB: everything for a
//! flavour with 4-byte pointers, and the slot for a 4-byte store.

use std::{collections::BTreeMap, path::Path};

use anyhow::{Context, Result, bail, ensure};
use serde_json::Value;

use crate::{
    layout::{self, EVEREST_FILE, Everest, FieldPath, Flavour, StringLayout},
    memory::{self, Arena},
};

/// The arena of objects and strings.
const ARENA_SIZE: usize = 1 << 20;

/// Each object's size. Every offset in the layouts is far below it.
const OBJECT_SIZE: u64 = 0x1000;

/// The path to the game's title, which the auto splitter checks for "Celeste".
const TITLE: &str = "Title";

/// Where to map what has to be below 4 GB, in order of preference.
const LOW: &[u64] = &[
    0x1000_0000,
    0x2000_0000,
    0x3000_0000,
    0x4000_0000,
    0x5000_0000,
];

/// Where to map the slot when it's above 4 GB. The fourth byte of each is 0xb8, the 4-byte Mono
/// store's opcode, so that the 4-byte pattern also matches inside an 8-byte store into it. The
/// auto splitter has to take the earlier match.
const HIGH: &[u64] = &[
    0x2_b800_0000,
    0x4_b800_0000,
    0x8_b800_0000,
    0x10_b800_0000,
    0x20_b800_0000,
    0x40_b800_0000,
];

/// How many bytes the fake puts between a signature and its store: more than the game has, 80
/// to 144 depending on the flavour, and none of those, so that only a search finds the store.
const FILLER: usize = 200;

/// What it puts there: int3, which no store pattern contains.
const FILLER_BYTE: u8 = 0xcc;

/// The fake game's memory, and where each value the reading test sets goes.
pub struct Game {
    arena: Arena,
    pointer_size: u64,
    /// How .NET strings are laid out, for a vanilla flavour.
    string: Option<StringLayout>,
    fields: BTreeMap<String, Field>,
}

/// A path or field that the reading test can set.
#[derive(Copy, Clone)]
enum Field {
    Int {
        address: u64,
        ty: Int,
    },
    /// A .NET `bool`: one byte.
    Bool {
        address: u64,
    },
    /// A flag of an Everest flag field: a bit of a `u32`.
    Flag {
        address: u64,
        bit: u32,
    },
    /// A pointer to a .NET string object, or null.
    DotnetString {
        address: u64,
    },
    /// A pointer to NUL-terminated UTF-8, with its length in bytes in the `u16` before it, or
    /// null.
    Utf8String {
        address: u64,
    },
}

#[derive(Copy, Clone)]
enum Int {
    U8,
    U32,
    I32,
    I64,
}

impl Game {
    /// Builds the game that a layout file describes. For a vanilla flavour, the store pattern is
    /// the one whose slot address has `store_width` bytes.
    pub fn build(file: &Path, store_width: Option<usize>) -> Result<Self> {
        if file.file_name().is_some_and(|name| name == EVEREST_FILE) {
            Self::everest(&layout::read(file)?)
        } else {
            Self::vanilla(&layout::read(file)?, store_width)
        }
    }

    fn vanilla(layout: &Flavour, store_width: Option<usize>) -> Result<Self> {
        let pointer_size = layout.pointer_size;
        ensure!(
            pointer_size == 4 || pointer_size == 8,
            "pointers of {pointer_size} bytes"
        );

        let roots = layout
            .roots
            .iter()
            .map(|(name, root)| {
                let store = match store_width {
                    Some(width) => root
                        .store
                        .iter()
                        .find(|store| layout::slot_width(store) == width),
                    None => root.store.first(),
                };
                let store = store.with_context(|| {
                    format!("{name} has no store pattern for a slot of that width")
                })?;
                Ok((name, root, store))
            })
            .collect::<Result<Vec<_>>>()?;
        let low = pointer_size == 4
            || roots
                .iter()
                .any(|(_, _, store)| layout::slot_width(store) == 4);
        let mut game = Self {
            arena: Arena::map(ARENA_SIZE, if low { LOW } else { HIGH })?,
            pointer_size,
            string: Some(StringLayout {
                length: layout.string.length,
                chars: layout.string.chars,
            }),
            fields: BTreeMap::new(),
        };

        // The code, built first and mapped once it's all written.
        let mut code = Vec::new();
        let mut slots = Vec::new();
        for (name, root, store) in roots {
            // The first slot is the arena's first allocation, at the address picked for it.
            let slot = game.arena.alloc(8)?;
            let object = game.arena.alloc(OBJECT_SIZE)?;
            game.write_pointer(slot, object)?;
            slots.push(format!(
                "{name} at 0x{slot:x}, with a {}-byte store",
                layout::slot_width(store)
            ));

            // First a copy whose store goes into a slot holding an object with another title,
            // which the auto splitter has to skip for the next match.
            let decoy_slot = game.arena.alloc(8)?;
            let decoy = game.arena.alloc(OBJECT_SIZE)?;
            game.write_pointer(decoy_slot, decoy)?;
            if let Some(path) = root.paths.get(TITLE) {
                let title = game
                    .build_path(decoy, path)?
                    .context("the title isn't a string")?;
                game.write(title, &Value::from("Not Celeste"))?;
            }
            for slot in [decoy_slot, slot] {
                code.extend(pattern_bytes(&root.signature, slot)?);
                code.extend([FILLER_BYTE; FILLER]);
                code.extend(pattern_bytes(store, slot)?);
            }

            for (path_name, path) in &root.paths {
                let field = game
                    .build_path(object, path)
                    .with_context(|| format!("building {path_name}"))?;
                if let Some(field) = field {
                    ensure!(
                        game.fields.insert(path_name.clone(), field).is_none(),
                        "two roots have the path {path_name}"
                    );
                }
            }
        }

        let code_address = memory::map(code.len(), &[])?;
        memory::write(code_address, &code);
        memory::make_executable(code_address, code.len())?;
        eprintln!(
            "fake game: {}: code at 0x{code_address:x}, slots: {}",
            layout.flavour,
            slots.join(", ")
        );

        if game.fields.contains_key(TITLE) {
            game.set(TITLE, &Value::from("Celeste"))?;
        }
        Ok(game)
    }

    /// Builds the objects along the path, and returns the field it ends in, if the reading test
    /// can set it.
    fn build_path(&mut self, mut object: u64, path: &FieldPath) -> Result<Option<Field>> {
        let (&last, hops) = path.offsets.split_last().context("no offsets")?;
        ensure!(
            path.types.len() == path.offsets.len(),
            "a type for each offset"
        );
        for &offset in &path.offsets {
            ensure!(
                offset + 8 <= OBJECT_SIZE,
                "offset {offset} is beyond the fake's objects"
            );
        }
        // Paths through the same fields share their objects.
        for &offset in hops {
            let field = object + offset;
            let mut next = self.read_pointer(field);
            if next == 0 {
                next = self.arena.alloc(OBJECT_SIZE)?;
                self.write_pointer(field, next)?;
            }
            object = next;
        }
        let address = object + last;
        Ok(Some(match path.types.last().map(String::as_str) {
            Some("System.Int32") => Field::Int {
                address,
                ty: Int::I32,
            },
            Some("System.Int64") => Field::Int {
                address,
                ty: Int::I64,
            },
            Some("System.Boolean") => Field::Bool { address },
            Some("System.String") => Field::DotnetString { address },
            // Any other object: it's there, but the reading test can't set it.
            _ => {
                if self.read_pointer(address) == 0 {
                    let next = self.arena.alloc(OBJECT_SIZE)?;
                    self.write_pointer(address, next)?;
                }
                return Ok(None);
            }
        }))
    }

    fn everest(layout: &Everest) -> Result<Self> {
        ensure!(layout.pointer_size == 8, "Everest's pointers are 8 bytes");
        let magic = layout
            .magic
            .split(' ')
            .map(|byte| u8::from_str_radix(byte, 16))
            .collect::<Result<Vec<_>, _>>()
            .context("parsing the magic")?;
        let size = layout
            .fields
            .values()
            .map(|field| field.offset + 8)
            .max()
            .unwrap_or(0);
        let info = memory::map(size as usize, &[])?;
        memory::write(info, &magic);
        eprintln!("fake game: everest: info struct at 0x{info:x}");

        let mut fields = BTreeMap::new();
        for (name, field) in &layout.fields {
            let address = info + field.offset;
            let int = |ty| Field::Int { address, ty };
            fields.insert(
                name.clone(),
                match field.ty.as_str() {
                    "u8" => int(Int::U8),
                    "u32" => int(Int::U32),
                    "i32" => int(Int::I32),
                    "i64" => int(Int::I64),
                    "utf8_ptr" => Field::Utf8String { address },
                    other => bail!("{name} has the unknown type {other}"),
                },
            );
            for (flag, &bit) in &field.flags {
                fields.insert(format!("{name}.{flag}"), Field::Flag { address, bit });
            }
        }

        let mut game = Self {
            arena: Arena::map(ARENA_SIZE, &[])?,
            pointer_size: layout.pointer_size,
            string: None,
            fields,
        };
        // The struct's version, which the auto splitter checks for.
        game.set("InfoVersion", &Value::from(layout.info_version))?;
        Ok(game)
    }

    /// Writes the value into the path or field with that name, as its type says. A string's
    /// value can be null.
    pub fn set(&mut self, name: &str, value: &Value) -> Result<()> {
        let field = *self
            .fields
            .get(name)
            .context("the layout has no such path or field, or the reading test can't set it")?;
        self.write(field, value)
    }

    fn write(&mut self, field: Field, value: &Value) -> Result<()> {
        match field {
            Field::Int { address, ty } => {
                let int = value.as_i64().context("expected an integer")?;
                let bytes = match ty {
                    Int::U8 => u8::try_from(int).map(|i| i.to_le_bytes().to_vec()),
                    Int::U32 => u32::try_from(int).map(|i| i.to_le_bytes().to_vec()),
                    Int::I32 => i32::try_from(int).map(|i| i.to_le_bytes().to_vec()),
                    Int::I64 => Ok(int.to_le_bytes().to_vec()),
                }
                .context("out of range")?;
                memory::write(address, &bytes);
            }
            Field::Bool { address } => {
                let flag = value.as_bool().context("expected a bool")?;
                memory::write(address, &[u8::from(flag)]);
            }
            Field::Flag { address, bit } => {
                let flag = value.as_bool().context("expected a bool")?;
                let flags = u32::from_le_bytes(memory::read(address));
                let flags = if flag {
                    flags | 1 << bit
                } else {
                    flags & !(1 << bit)
                };
                memory::write(address, &flags.to_le_bytes());
            }
            Field::DotnetString { address } => {
                let string = match string_value(value)? {
                    Some(text) => {
                        let layout = self.string.as_ref().context("no string layout")?;
                        let chars = text.encode_utf16().collect::<Vec<_>>();
                        let size =
                            (layout.length + 4).max(layout.chars + 2 * chars.len() as u64 + 2);
                        let object = self.arena.alloc(size)?;
                        memory::write(
                            object + layout.length,
                            &i32::try_from(chars.len())?.to_le_bytes(),
                        );
                        let bytes = chars
                            .iter()
                            .flat_map(|c| c.to_le_bytes())
                            .collect::<Vec<_>>();
                        memory::write(object + layout.chars, &bytes);
                        object
                    }
                    None => 0,
                };
                self.write_pointer(address, string)?;
            }
            Field::Utf8String { address } => {
                let string = match string_value(value)? {
                    Some(text) => {
                        let buffer = self.arena.alloc(2 + text.len() as u64 + 1)?;
                        memory::write(buffer, &u16::try_from(text.len())?.to_le_bytes());
                        memory::write(buffer + 2, text.as_bytes());
                        buffer + 2
                    }
                    None => 0,
                };
                self.write_pointer(address, string)?;
            }
        }
        Ok(())
    }

    fn write_pointer(&self, address: u64, pointer: u64) -> Result<()> {
        if self.pointer_size == 4 {
            let pointer = u32::try_from(pointer).context("a 4-byte pointer above 4 GB")?;
            memory::write(address, &pointer.to_le_bytes());
        } else {
            memory::write(address, &pointer.to_le_bytes());
        }
        Ok(())
    }

    fn read_pointer(&self, address: u64) -> u64 {
        if self.pointer_size == 4 {
            u32::from_le_bytes(memory::read(address)).into()
        } else {
            u64::from_le_bytes(memory::read(address))
        }
    }
}

fn string_value(value: &Value) -> Result<Option<&str>> {
    match value {
        Value::Null => Ok(None),
        Value::String(text) => Ok(Some(text)),
        _ => bail!("expected a string or null"),
    }
}

/// A pattern's bytes, with the slot's address in its `SS` bytes, and zero for any byte.
fn pattern_bytes(pattern: &str, slot: u64) -> Result<Vec<u8>> {
    ensure!(
        layout::slot_width(pattern) != 4 || slot <= u64::from(u32::MAX),
        "a 4-byte store into 0x{slot:x}"
    );
    let mut slot_bytes = slot.to_le_bytes().into_iter();
    pattern
        .split(' ')
        .map(|byte| match byte {
            "??" => Ok(0),
            "SS" => slot_bytes.next().context("more than 8 SS bytes"),
            hex => u8::from_str_radix(hex, 16).with_context(|| format!("{hex} in {pattern}")),
        })
        .collect()
}
