# Security

## Reporting a vulnerability

Please don't open a public issue for a security problem. Report it privately instead:
**[Report a vulnerability](https://github.com/Shuiei/valheim-flowing-water/security/advisories/new)**
(the repository's Security tab, then **Report a vulnerability**).

Say what is affected (the server, the players' game, or both), the version, and how to reproduce it.
Once it is fixed, the fix is released and the advisory published, with credit to you if you want it.

## What is in scope

The plugin on a server and in the players' game: the water data the server saves in the world and
sends to players, the console command, and the config. Anything that lets a player or a world file
crash or freeze a server or another player's game, run code, or write outside the game's folders is
a vulnerability.

## Checking a download

Each file on the [releases page](https://github.com/Shuiei/valheim-flowing-water/releases) has a
`<file>.sigstore.json` beside it: its [Sigstore](https://www.sigstore.dev/) signature, made by this
repository's release workflow (`.github/workflows/sign-release.yml`). With
[cosign](https://docs.sigstore.dev/cosign/system_config/installation/), download both and run:

```sh
cosign verify-blob FlowingWater-0.5.0.zip \
  --bundle FlowingWater-0.5.0.zip.sigstore.json \
  --certificate-identity-regexp '^https://github.com/Shuiei/valheim-flowing-water/\.github/workflows/sign-release\.yml@' \
  --certificate-oidc-issuer https://token.actions.githubusercontent.com
```

`Verified OK` means the file is the one the release was signed with, byte for byte.

## Supported versions

Only the [latest release](https://github.com/Shuiei/valheim-flowing-water/releases/latest) gets
security fixes. Use the same version on the server and on every player's game.
