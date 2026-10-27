# Character creation after a fresh checkout

The reported symptom is a missing character picture on the real character creation screen after another computer pulls `main`. Resource delivery alone did not verify this screen.

## Confirmed defect

The Editor branch of `QdaoOriginalHdResourceIndex.Validate` required each PNG's local modification time to equal `sourceWriteUtcTicks` recorded on the producing machine. Git does not preserve those times. All 1,507 images in the 11 delivered V14 inventories have different local timestamps in the investigated checkout even though their signed bytes match. An invalid HD inventory makes `ResolveAppearance` return null; the creation screen filtered those identities and silently disabled its preview image. This is a reproducible cross-checkout defect, not proof that every possible missing-picture report has the same cause. The timestamp gate was Editor-only.

## Resulting behavior

- The default Editor inventory provider verifies GUID, byte count, PNG dimensions and SHA256 instead of requiring the producer's timestamp. Verification is cached only within the existing resource import revision. Same-length edits cannot pass an actual file check by restoring the old timestamp.
- The Editor index builder treats timestamp-only differences as equivalent, so a fresh checkout does not require rewriting the committed inventories.
- The active creation page refreshes after resource imports while retaining the selected identity and typed name. A missing preview retries once per second until ready. The first available identity is selected when an initially empty roster becomes usable; an explicit profession default stays explicit.
- A missing portrait displays a preparation message and blocks creation, including direct submit callbacks. Available identities show a position/count beside the appearance arrows. The submission checks both appearance availability and the actual portrait.

The 15 character identities, 20 pets and 519 map tiles retain their existing resource bytes and gameplay routes. This change does not generate art, change map selection/acceptance, change phone settings, install software or modify server protocols.

## Validation

`dotnet run --project tools/tests/character_availability/CharacterAvailability.csproj --configuration Release -- .` links the actual production file verifier without Unity. It runs 17 real-file regression checks and validates all 11 inventories / 1,507 PNGs (1,143,504,920 bytes), including contract hashes and asset GUIDs. In the investigated checkout, all 1,507 legacy timestamps differ and all signed source identities pass. The regression includes unchanged bytes with a changed timestamp, equal-length content edits with a forged original timestamp, missing files, wrong lengths/dimensions/hashes and invalid PNG headers. The same command runs in both Windows and Ubuntu resource CI jobs.

All 15 delivered portraits were independently decoded: each is 1024 by 1024 and contains visible pixels. Runtime code (414 source files) compiles for Editor and Release against Unity 6000.6.0f1 assemblies; all four Editor tools and the affected EditMode/PlayMode test sources also compile.

Unity tests were added for the real default Editor file provider, timestamp-only index equivalence, and creation-page recovery/identity/name preservation. They have not been executed in the engine on this machine: the isolated Unity startup probe stopped at LicensingClient signature validation failure (Code 10). No license/security settings were changed. There is no claim of a rendered creation-screen screenshot, another-computer acceptance, or a network/gameplay acceptance test.

Detailed local logs and machine-readable evidence are retained in the ignored `.codex-artifacts/character-creation-20261027/` directory. Update the client source on the other computer, allow Unity to finish importing, and rebuild any existing standalone executable before evaluating this code change.
