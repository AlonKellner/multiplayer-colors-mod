# Publishing to the Steam Workshop

Published with megacrit's **`ModUploader` CLI** (a Steamworks wrapper), not through the game.
Same process as the sibling `the-apprentice-mod` repo, minus the PCK — this is a DLL-only mod.

- **Live item:** `Multiplayer Colors WIP` — see `MultiplayerColors/mod_id.txt` for the ID
- **Visibility:** `unlisted` (hidden from search; anyone with the link can Subscribe)

## Layout

```
workshop/
├── .gitignore                 # ignores per-upload build outputs + logs
├── README.md                  # this file
└── MultiplayerColors/         # the upload "workspace"
    ├── workshop.json          # store metadata + visibility (TRACKED)
    ├── image.png              # Workshop thumbnail, must be <1MB (TRACKED)
    ├── mod_id.txt             # Steam item ID, written on first upload (TRACKED — do not delete)
    └── content/               # the actual mod files uploaded (build outputs, gitignored)
        ├── MultiplayerColors.json
        └── MultiplayerColors.dll   # NOTE: no .pdb in a release upload
```

The `ModUploader` binary is not vendored here. Point `MOD_UPLOADER` at an existing copy — the
sibling repo has one at `../the-apprentice-mod/tools/mod-uploader` — or download
`ModUploader-osx-arm64.zip` from https://github.com/megacrit/sts2-mod-uploader/releases and clear
the Gatekeeper quarantine with `xattr -dr com.apple.quarantine <dir>`.

## Publishing / updating

**Steam must be running and logged in** — the uploader publishes under the logged-in account.

```bash
scripts/publish-workshop.sh                 # test -> build -> assemble -> upload
scripts/publish-workshop.sh "change note"   # also update workshop.json changeNote first
SKIP_TESTS=1 scripts/publish-workshop.sh    # skip the test gate
```

`mod_id.txt` makes every subsequent run update the **same** item. Deleting it would create a
duplicate item on the next upload.

On success the tool prints the item URL. On failure, read `mod-uploader.log` next to the binary.

## `workshop.json` fields

| Field                | Notes |
|----------------------|-------|
| `title`              | Store title. |
| `description`        | Store description. |
| `visibility`         | `private` \| `friends_only` \| `unlisted` \| `public`. `unlisted` = link-only. |
| `changeNote`         | Shown to subscribers for this update. |
| `tags`               | Search tags. |
| `dependencies`       | Workshop item IDs of required mods. Empty — this mod needs no BaseLib. |
| `contentDescriptors` | Mature-content flags; empty here. |

Most fields can be set to `null` / omitted to leave them unchanged on re-upload.
