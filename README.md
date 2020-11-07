# Horses!
## Basic ownership management for Rust rideable horses

Tired of some clown stealing the horse you just bought or found?  This plugin allows players to maintain ownership and optionally prevent others from riding off with them.

Rideable horses may be claimed by mounting or via the chat command /hclaim.

Claimed horses can be released via the chat command /hrelease or through the use of a timer configured by the admin.

Users with permission may also spawn and remove their owned horses.

Limits may be set for standard and VIP users.

### Configuration
```json
{
  "Options": {
    "useClans": false,
    "useFriends": false,
    "useTeams": false,
    "SetOwnerOnFirstMount": true,
    "EnableTimer": false,
    "ReleaseTime": 600.0,
    "ReleaseOwnerOnHorse": false,
    "RestrictMounting": false,
    "EnableLimit": true,
    "Limit": 2.0,
    "VIPLimit": 5.0
  },
  "Version": {
    "Major": 1,
    "Minor": 0,
    "Patch": 2
  }
}
```

- `useClans/useFriends/useTeams` -- Use Friends, Clans, or Rust teams to determine accessibility of an owned horse.  This allows friends to share horses.
- `SetOwnerOnFirstMount` -- Sets ownership of an unowned horse on mount.
- `EnableTimer` -- Enable timed release of horse ownership after the time specified by ReleaseTime.
- `ReleaseTime` -- Sets the time IN SECONDS to maintain horse ownership if EnableTimer is true.  Must be a numerical value.
- `ReleaseOwnerOnHorse` -- Release ownership of a horse while the owner is mounted after ReleaseTime has been reached.
- `RestrictMounting` -- Restrict mounting of owned horses to the owner.  If false, you can use other plugins to manage this such as PreventLooting.
- `EnableLimit` -- Enable limit of total claimed horse count per player.
- `Limit` -- Limit for users with claim permission.
- `VIPLimit` -- Limit for users with vip permission.

### Permissions

- `horses.claim` -- Allows player claim and release horses.
- `horses.spawn` -- Allows player to spawn or remove a horse.
- `horses.vip` -- Gives player vip limits when limit is in use.

## Commands

- `/hclaim` - Claim the horse you're looking at (requires horses.claim permission).  If the horse is owned by the server, this should work.  However, this will not bypass the purchase routine at the stables.
- `/hrelease` - Release ownership of the horse you're looking at (requires horses.claim permission).
- `/hspawn` - Spawn a new horse in front of you (requires horses.spawn permission).
- `/hremove` - Kill the horse in front of you (requires horses.spawn permission and ownership of the horse).  You may then enjoy some delicious horse meat.

