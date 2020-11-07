# Horses!
## Basic ownership management for Rust rideable horses

Tired of some clown stealing the horse you just bought or found?  This plugin allows players to maintain ownership and optionally prevent others from riding off with them.

Rideable horses may be claimed by mounting or via the chat command /hclaim.

Claimed horses can be released via the chat command /hrelease or through the use of a timer configured by the admin.

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
    "RestrictMounting": false
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

### Permissions

- `horses.claim` -- Allows player claim and release horses

## Commands

- `/hclaim` - Claim the horse you're looking at.  If the horse is owned by the server, this should work.  However, this will not bypass the purchase routine at the stables.
- `/hrelease` - Release ownership of the horse you're looking at.

