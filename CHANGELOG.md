# Changelog

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
