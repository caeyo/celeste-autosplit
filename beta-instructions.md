Welcome to the celeste-autosplit beta. Some reasons why you might want to try it over the existing autosplitter:
- Cross-OS, compatible with any app that uses livesplit-core (LiveSplit on Windows, LiveSplit One OBS, the soon-to-be-released LiveSplit One Desktop, etc.). Works in vanilla and Everest
- Exactly game-accurate times - no more non-divisible by 0.017
- Fixes garbage memory reads - symptoms of this were needing to restart livesplit to pick up the game or seeing non-sensical values on the timer
- Reset and start behaviour overhaul where you now can choose the reset and start triggers. This allows for:
   - Auto resetting in fullgame
   - Auto resetting in ILs with S+Q
   - Better compatibility with SRTool segments (you can now auto reset and clearly set when to start the timer)
- Add new info to your layout like death count, strawberry count, etc. via custom variables

## LiveSplit Windows

To install the beta, download this script <https://github.com/caeyo/celeste-autosplit/releases/download/v0.1.0/overwrite-autosplitter-list.cmd>, put it next to your LiveSplit.exe, and run it. That should be all you need to do - in fact to start off with I'd appreciate if you didn't even open the settings menu and just did a couple runs to check that your splits still behave as expected without any input on your end.

The script overwrites the global list of autosplitters to point Celeste’s entry to this repo and prevents LiveSplit from updating it by marking the file as read-only. This means you won't get automatic updates to the list so long as you've got the beta active - if you do need to refresh the list, rerunning the script will allow you to do so manually. You can also rerun the script to undo the changes and go back to the default list (this will take you back to the old autosplitter, which requires using your backed up splits - see below for notes on the migration process).

## Other livesplit-core apps

Download this file <https://github.com/caeyo/celeste-autosplit/releases/download/v0.1.0/celeste_autosplit.wasm> and add it to the app - I don't actually know how most of those apps work but I know (in theory) the autosplitter will work with them.

## Notes

LiveSplit on Windows has the usual in-app settings UI. For other apps, the current support for building settings menus is extremely limited, and so this site is provided to edit autosplitter settings in a .lss file: <https://caeyo.github.io/celeste-autosplit>

Settings are incompatible between this autosplitter and the existing one:
- Migration is handled for you in LiveSplit Windows. A backup of your splits is made during migration next to the splits file in case of emergency or if you want to go back to the old autosplitter (it's tagged as `[BACKUP]`, hard to miss).
- Otherwise, the website migrates files.

If you want to take advantage of the new auto reset behaviour, go into the settings and apply the relevant preset. Note that for full game and IL with s+q, your run will reset and start on the same update, so you won’t see the visual gap on the timer between resetting and the timer starting - this is fine, you’ll still see the segments reset (trust me that this is the best solution I could come up with).

To use the custom variables, add a Text component to your layout, go to Layout Settings, check the box for Custom Variable and put in the ID - the useful ones are probably `Strawberries` and `Deaths` but the full list is in [this function](https://github.com/caeyo/celeste-autosplit/blob/ba64a80217706ff6c74f13d15fec874a6d457834/autosplitter/src/lib.rs#L11).

Feedback on any bugs, especially negative differences with the existing autosplitter, is very much appreciated. I’m also more than happy to take feature requests now for new types of splits, custom variables, etc. - anything you can dream up, for example one I think I’ve already figured out is splitting on key collection. Contact me on Discord at `caeyo.`.
