# Changelog

## Unreleased

### Reporting problems
- A bug icon at the bottom right of the window creates a report zip on the Desktop: the player's description, the state of the
  editor and game, and the run logs. Every write the editor makes to the game is logged to disk as it happens, so a report can
  also be made after the editor or the game was closed or crashed. Nothing is uploaded.

### Players
- **Future transfer** from Move to club (untick Move now): a date and every transfer term in FMRTE's order (instalments,
  league / international / per-appearance and goal payments, sell-on with Of the profit, buy-back fee, wage contribution,
  friendly), all 0 by default. Game Pass only for now.
- **Remove future transfer**: a button on the Future Transfer tab takes the agreed transfer back out of the game (on both builds).
- **Swap Player**, **Promises** (all 68 types, add / edit / remove), **Tactical Understanding** preset.
- **Favourites**: star a player, then open the favourites table from the toolbar.
- **Reload** (Ctrl+R) rereads the current screen from the game and keeps unsaved edits.

### Stadiums and cities
- Stadium nearby stadium and coordinates; owner and location open the club and the city.
- City nation, region, language and weather.

### Other
- **Settings** (gear button in the toolbar): the date format for the whole editor, DD-MM-YYYY (default), MM-DD-YYYY or YYYY-MM-DD, remembered between runs. Exports keep ISO dates.
- **Light theme** (white and blue) next to the dark one: the sun / moon button in the toolbar switches in place and the choice is remembered.
- Notices: the game runs a supported build but no save is loaded; a newer release exists on GitHub (one check at start); the game loaded another save, which locks every tab and refuses writes until Load Game reads it.
- Much lower memory use for pictures (smaller decoded images, a capped cache, a compact index of the graphics pack).
- Smaller download: native libraries for other platforms are left out of the publish folder.
- New logo and icon.
- Removed Mass Edit and the club-level Tactical Understanding bars.

## 0.3.0 (5/11/2026)

### Game versions
- FM27 `27.0.0+2493426` on **Steam** (the November update).
- FM27 on **Xbox / Game Pass** (package `0.9.2409.0`). Steam and Game Pass share the object layout; the Game Pass
  profile maps every class by its RTTI name. `re/tools/gamepass-rva-map.ps1` rebuilds a profile for a new build in seconds.
- Steam and Game Pass report the same build string, so builds are told apart by image size and PE timestamp.

### Players
- Information: nationality, city of birth, nationality info, body, hair, skin, ethnicity, days in country, flags.
- Contract and future contract, bonuses and clauses, transfer clauses (sell-on, appearance fee, competition bonus).
- Career Plans, Relationships (with Other Nationality), Languages, Preferred Moves, Positions.
- Injuries, bans, unavailability, unhappiness, national call-ups (swap), transfer proposals, future transfers.
- Comparison, Clone Center.
- **Release Player**, with a preview and Undo. Clubs run by a human manager are refused.
- **Captain / vice captain**.
- **Move to club** now updates both team links of the contract.

### Staff
- New Staff screen: information, contract, Career Plans, Relationships, Languages, Knowledge (edit, add, remove),
  Formations, Tendencies.
- **Move staff to another club** (coaching, medical, scouting), with Undo.

### Clubs
- Relationships, Staff by club, Squads with presets, Transfer Center, Debt, Affiliations, Club Vision importance.

### Nations, stadiums, cities, competitions, awards
- New screens for each. Nation details, teams' reputation, relationships, transfer preferences; stadium details and
  flags; city attraction, location and altitude; competition and award reputation.

### Tools
- **Mass Edit** for players, staff and clubs: attributes, CA/PA, reputation, height, positions, personality, condition,
  wages and more. Every batch is previewed and undone as one.
- **Presets** for a whole team: Heal, Fitness, Morale, Lift bans, Remove Unhappiness, Max attributes,
  Prevent retirement.
- **Freezer** for player attributes and club balance. It is kept per save and re-applied when you connect again.
- **Export** the player list, with its filters, to CSV or JSON.
- Filters: attribute ranges for players; name, club and job for staff.

### Other
- Licensed under GPL-3.0-or-later.
- Support links: Ko-fi, and VietQR in the Vietnamese README.
