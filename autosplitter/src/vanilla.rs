use asr::PointerSize;

pub struct Pattern {
    pub before: &'static [Option<u8>],
    pub width: usize,
    pub after: &'static [Option<u8>],
}

pub struct Anchor {
    pub signature: &'static str,
    pub references: &'static [Pattern],
    pub reference_window: u64,
}

pub struct Layout {
    pub pointer_size: PointerSize,
    pub string_length: u64,
    pub string_chars: u64,
    pub celeste_instance: Anchor,
    pub auto_splitter_info_store: Pattern,
    pub auto_splitter_info_store_window: u64,
    pub save_data_instance: Anchor,
    pub chapter: u64,
    pub mode: u64,
    pub level: u64,
    pub timer_active: u64,
    pub chapter_started: u64,
    pub chapter_complete: u64,
    pub chapter_time: u64,
    pub chapter_strawberries: u64,
    pub chapter_cassette: u64,
    pub chapter_heart: u64,
    pub file_time: u64,
    pub file_strawberries: u64,
    pub file_cassettes: u64,
    pub file_hearts: u64,
    pub total_deaths: u64,
}

pub const CLR32: Layout = Layout {
    pointer_size: PointerSize::Bit32,
    string_length: 0x4,
    string_chars: 0x8,
    celeste_instance: Anchor {
        signature: "68 38 04 00 00 68 c0 03 00 00 68 1c 02 00 00 ff 35",
        references: &[Pattern {
            before: &[Some(0x8d), Some(0x15)],
            width: 4,
            after: &[Some(0xe8)],
        }],
        reference_window: 256,
    },
    auto_splitter_info_store: Pattern {
        before: &[Some(0x8d), Some(0x96)],
        width: 4,
        after: &[Some(0xe8)],
    },
    auto_splitter_info_store_window: 128,
    save_data_instance: Anchor {
        signature: "75 06 33 c9 33 d2 eb 09 8b 47 2c 8b 48 04 8b 50 08 89 4e 04 89 56 08",
        references: &[Pattern {
            before: &[Some(0x8b), Some(0x0d)],
            width: 4,
            after: &[Some(0x85), Some(0xc9)],
        }],
        reference_window: 256,
    },
    chapter: 0x18,
    mode: 0x1c,
    level: 0x14,
    timer_active: 0x30,
    chapter_started: 0x31,
    chapter_complete: 0x32,
    chapter_time: 0x4,
    chapter_strawberries: 0x20,
    chapter_cassette: 0x33,
    chapter_heart: 0x34,
    file_time: 0xc,
    file_strawberries: 0x24,
    file_cassettes: 0x28,
    file_hearts: 0x2c,
    total_deaths: 0x30,
};

pub const MONO64: Layout = Layout {
    pointer_size: PointerSize::Bit64,
    string_length: 0x10,
    string_chars: 0x14,
    celeste_instance: Anchor {
        signature: "be 80 07 00 00 ba 38 04 00 00 b9 c0 03 00 00 41 b8 1c 02 00 00",
        references: &[
            Pattern {
                before: &[Some(0xb8)],
                width: 4,
                after: &[Some(0x4c), Some(0x89), Some(0x38)],
            },
            Pattern {
                before: &[Some(0x48), Some(0xb8)],
                width: 8,
                after: &[Some(0x4c), Some(0x89), Some(0x38)],
            },
        ],
        reference_window: 256,
    },
    auto_splitter_info_store: Pattern {
        before: &[Some(0x49), Some(0x8d), Some(0x87)],
        width: 4,
        after: &[],
    },
    auto_splitter_info_store_window: 128,
    save_data_instance: Anchor {
        signature: "75 04 33 ed eb 0b 49 8b 46 58 48 8b a8 98 00 00 00 49 89 6c 24 28",
        references: &[
            Pattern {
                before: &[Some(0x48), Some(0x8b), Some(0x04), Some(0x25)],
                width: 4,
                after: &[],
            },
            Pattern {
                before: &[Some(0x48), Some(0xb8)],
                width: 8,
                after: &[Some(0x48), Some(0x8b), Some(0x00)],
            },
        ],
        reference_window: 256,
    },
    chapter: 0x18,
    mode: 0x1c,
    level: 0x10,
    timer_active: 0x20,
    chapter_started: 0x21,
    chapter_complete: 0x22,
    chapter_time: 0x28,
    chapter_strawberries: 0x30,
    chapter_cassette: 0x34,
    chapter_heart: 0x35,
    file_time: 0x38,
    file_strawberries: 0x40,
    file_cassettes: 0x44,
    file_hearts: 0x48,
    total_deaths: 0x80,
};
