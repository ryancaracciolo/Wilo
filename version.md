# Shipping a version

1. **Wilo → Version → Bump Patch** (or Bump Minor). Updates Player Settings and `latest` in `version.json`.
2. Edit `version.json`: itch URL + a one-line `notes`. Leave `minimum` alone unless old builds must be blocked (**Require This Build**).
3. Make the Windows/Mac build.
4. Upload it to itch. Wait until it is live.
5. Push `version.json` to `main` **after** itch is updated.

`Get the update` only shows if `url` is set. The porch fetches this repo’s raw `version.json` — that only works if the repo is public (otherwise put a public gist URL on IntroHud).

People already on a version-check build get the porch card next launch. Anyone on an older build needs this current build once.
