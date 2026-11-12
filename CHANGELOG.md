# Changelog

## 1.3.0

### Game builds
- Supports the Steam hotfix `27.0.1+2503222`. Xbox / Game Pass stays on `27.0.1+2494687`; both run every reader and write, and pass
  the same calibration checks.

### Staff
- **Training Rating** tab: the eleven ratings as sliders (0–5 stars); saving sets the attributes behind a rating.
- Information shows and edits **Caps**, **Goals**, **License** and the fifteen **Roles** ratings.

### Players
- **Make Captain** / **Make Vice Captain** work in one click.
- Each child tab (Relationships, Contract, Languages…) shows a * while it holds unsaved edits.

### Clubs
- **Status** (Professional, Semi Professional, Amateur) and the full Ownership Type, Chairperson Title and Rich Benefactor lists.
- International pitch size reads as empty when the box is unticked.

### Mass Edit
- Rebuilt like FMRTE: coach General page (Age, Nationality, Caps, Goals, License, Training Rating), player Age and Nationality,
  club Status, Reputation, ticket price, Capacity and pitch sizes.
- No rows cut off at the bottom; opens without a pause.
- Fixed: a field set in one scope was also planned for another scope with the same field number (a club's Balance was
  offered as a nation's Women FIFA Ranking Points). Each scope now plans only its own fields. If you used Apply all with
  fields set in several scopes on 1.1.0–1.2.0, check the clubs and nations involved.
- Money boxes accept 1.000.000.000, 1,000,000,000 or plain digits; the review closes after Apply; Choose who to edit
  opens at once.

### Other
- Wonderkid Academy is marked Experimental and has a How to use guide. Force Accept Transfer is withdrawn until it is redone
  safely (shown as Coming Soon).
- Money on the club screen and in club Mass Edit is shown and typed in GBP, EUR, USD or VND: the game's own currency
  (detected) or one you pick in Settings. The game keeps pounds; limits follow the chosen currency.
- Open player tabs follow you to a newly loaded save (matched by player ID) instead of showing an empty page.
- Clearer tooltips; Freezer Manager layout fixed.

## 1.2.0

### Game builds
- Supports Football Manager 27 `27.0.1+2494687` on Xbox / Game Pass and Steam. Every reader and every kind of write was run against
  the live game on both.

### Players
- **Development speed** (Tools card of a player): makes the player improve faster from training while the game runs.
- **Transfer Proposals** shows loan, trial and B club move offers next to transfer offers, and their value can be edited.
- **Relationships** shows the name of every person a player likes or dislikes.

### Clubs and squads
- Squad presets: **Grant work permit** gives the ticked players a permit for the club's own country; **Golden Generation** sits on the
  first team and applies to every other team of the club. Every preset has a tooltip.
- Club Vision and Squads open without freezing the window.
- Stadium names and details are read correctly.
- Players currently out on loan are listed again.

### Nations
- National-team staff show their job and team for every member, not only those on a full contract.

### Other
- The editor notices a newly loaded save more reliably, and the status bar date follows the game.
- Work Permit opens at once (the permit table is found in the background after Load Game).
- Larger app icon.

## 1.1.0

### Mass edit
- **Mass edit** (toolbar button or Ctrl+M) changes many players, coaches, clubs or nations at once, in FMRTE's layout, with a Nation scope
  (youth rating and the other nation fields) that FMRTE does not have. Every change goes through the edit history, so it can be undone.

### Search
- **Advanced search** is rebuilt and filters players, coaches and clubs, with a results window.

### Players and staff
- **Relationships** lists every one of FMRTE's 21 relation types (Dislikes Team, Competition Unregistration, Invited to represent nation,
  Developed at Country, Other Nation Caps and Youth Caps with their Caps and Goals, and the rest), reads rows the game names "Unknown (N)",
  and adds and removes any of them.

## 1.0.1

### Staff
- **Move to club** can set the role and the weekly wage the staff member gets at the new club. A role from another department
  (coaching, medical, scouting) puts the member in that department of the club. The move takes effect on the game's current day.

### Players
- **Prevent retirement** (Tools card of a player) now applies at once: it removes the planned retirement, sets the keep-playing
  traits, reloads the screen and says what it did in the status bar. A player with nothing to change gets a message instead of an empty window.
- **Career Plans**: Add puts an empty row in the list, Remove takes a row out, and nothing is written until the one Save.
- **Clone Center** searches every player, not only the ones shown in the Players table.
- The Discard buttons are gone; Save is the global Save, Reload (Ctrl+R) drops unsaved edits.
- A message shown after a save stays in the status bar instead of being cleared when the screen redraws.

## 1.0.0

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
