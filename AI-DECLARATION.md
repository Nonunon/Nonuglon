---
version: "0.1.2"
level: copilot
processes:
  design: pair
  implementation: copilot
  documentation: copilot
  testing: none
  review: pair
  deployment: copilot
---

This format is based on [AI-DECLARATION.md](https://ai-declaration.md/en/0.1.2).

## Notes

- The vast majority of this project's code was written by Claude (Anthropic), acting
  on direction from the repo owner: Nonunon.
- Tweak selection and scope were human-driven: the owner chose which specific
  behaviors to port from ffxiv-bundleoftweaks and PandorasBox ("just the handful of
  things I actually wanted"), rather than the AI proposing scope independently -
  hence `design: pair`.
- Implementation (Tweaks/, Plugin.cs, Configuration.cs, Windows/) was carried out by
  the AI against that direction, including command handling, config wiring, and
  bugfixes.
- No formal automated test suite exists yet, hence `testing: none`.
- CI/CD (`repo.json`, `.github/workflows/release.yml`, the `build/` scripts) was
  authored by the AI: the custom Dalamud plugin repository, the tag-triggered
  release workflow, and the local build/release helper scripts, hence
  `deployment: copilot`.
- The owner reviews and directs all changes; nothing is committed without their
  prompt or approval.
