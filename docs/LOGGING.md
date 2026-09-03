# Plugin log files

ArkoviaEconomy writes its own messages to:

`tshock/ArkoviaEconomy/logs/arkovia-YYYY-MM-DD.log`

This is the plugin's configuration/data folder (under the configured TShock save directory), not the folder holding the DLL. It is initialized before config loading. ConsoleInfo, ConsoleWarn and error calls formerly sent through TShock now use this destination, including NPC decisions, funding polling, event tracking, commands and transfer retries.

Timestamps are UTC. Files rotate daily and when they reach 10 MiB; size-rotated files have a unique suffix. Plugin log files older than 14 days are removed during an hourly check when logging occurs. Writes are serialized between game/background threads and appended with no persistent open handle. Newlines in messages are escaped to preserve one record per line. `GameplayEconomy.LogNpcRewardDecisions` still controls verbose NPC decision output.

If the folder cannot be created at startup, initialization fails visibly. If later writes fail, a console error is emitted at most once every ten minutes so the operator can repair the destination. Normal plugin operation emits no routine console logs. TShock's own command auditing and messages from other plugins are controlled separately.
