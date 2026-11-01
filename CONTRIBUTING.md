# Contributing to WdiCheck

Bug reports, driver mismatches and pull requests are welcome.

## Reporting a problem

Open an issue with:

- What WdiCheck showed and what you expected, e.g. "says 120 days behind, but the vendor's page
  lists my version as the newest".
- The device and driver involved. **Export** on the Overview saves a JSON or CSV report. It
  includes the computer name and the board and BIOS identifiers, so remove anything you'd rather
  not share before attaching it.

Wrong or missing *data* (a family, a version, a board's listing) usually belongs to
[WinDriverIndex](https://github.com/dtucny/WinDriverIndex/issues) rather than here. If you're not
sure which, open it here.

## Pull requests

- Build and test with `./scripts/build.ps1` (see [Building](README.md#building)). The tests
  must pass.
- Match the surrounding code: naming, comment density and idiom.
- Add a test for matching or version-comparison changes. Fixtures in
  `tests/WdiCheck.Core.Tests` are built from real index rows; keep new ones that way.
- Note user-visible changes under `## [Unreleased]` in [CHANGELOG.md](CHANGELOG.md).

## Licence of contributions

WdiCheck is licensed under the GNU GPL version 3 or later with the additional terms in
[NOTICE](NOTICE). By submitting a contribution you agree that:

- it is your own work, or you have the right to submit it;
- it is licensed under those same terms; and
- D. Tucny may also distribute it under other terms, such as in the Microsoft Store build or under
  a later licence for WdiCheck as a whole.

The last point keeps a single owner able to relicense the project. If you can't agree to it,
say so in the pull request before it's merged.
