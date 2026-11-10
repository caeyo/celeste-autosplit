# celeste-autosplit

An auto splitter for [Celeste](https://www.celestegame.com/). 

This auto splitter runs on the latest version of the game on Windows, Linux and macOS. It works on XNA and FNA, and with Everest.

It can be used in any [livesplit-core app](https://github.com/LiveSplit/livesplit-core#projects-using-livesplit-core), including LiveSplit (Windows) and LiveSplit One.

A website for editing the auto splitter settings in your splits file is available at [https://caeyo.github.io/celeste-autosplit](https://caeyo.github.io/celeste-autosplit).

## Installing

On LiveSplit (Windows) a component automatically downloads and manages this auto splitter - do not manually install it yourself.

Other livesplit-core apps, such as LiveSplit One, should read the global auto splitter list and install it for you as well. If you need to manually install it, download [celeste_autosplit.wasm](https://github.com/caeyo/celeste-autosplit/releases/latest/download/celeste_autosplit.wasm) and activate it via whatever means the app uses.

## Migrating from LiveSplit.Celeste

This auto splitter replaces the original [LiveSplit.Celeste](https://github.com/ShootMe/LiveSplit.Celeste/) auto splitter, and settings are incompatible between the two.

If you are using LiveSplit on Windows, the component will automatically migrate any splits file that used LiveSplit.Celeste as soon as the file is loaded. No action is needed to make this migration.

Otherwise, legacy splits files can be migrated by opening them on the [splits editor website](https://caeyo.github.io/celeste-autosplit), clicking migrate when prompted, then saving the new file.

## Building, Contributiing

See the [maintaining doc](https://github.com/caeyo/celeste-autosplit/blob/master/maintaining.md).
