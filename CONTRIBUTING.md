# CONTRIBUTIONS.md

## Introduction

We're glad you're interested in contributing to our project! While the maintainers of this project are internal team members, we warmly welcome contributions from the community in the form of bug reports, enhancement suggestions, and documentation improvements.

This document outlines how you can contribute to our project and what you can expect during this process. Please read it thoroughly before you begin.

## Community

Join the **J-Tech JAPAN OSS Discord** to ask questions, discuss ideas, and connect with other Sekiban users and contributors. There is a dedicated channel for the Sekiban community.

👉 [Join our Discord](https://discord.gg/kMdv978X)

## Code of Conduct

First and foremost, participants in this project are expected to respect our [Code of Conduct](CODE_OF_CONDUCT.md). We're committed to providing a welcoming and positive experience for all contributors, so please respect these guidelines.

## Reporting Bugs

We're constantly striving to improve, and your bug reports are a significant part of that. If you've identified a bug, please open an issue in our GitHub repository, providing as much detail as possible. Here's what we'd like you to include in your bug report:

- A clear and concise description of the bug.
- Steps to reproduce the issue.
- Expected behavior.
- Screenshots (if applicable).
- Any other information that might help us understand and resolve the issue.

## Suggesting Enhancements

If you have an idea that could improve our project, we'd love to hear about it! Please open an issue in our GitHub repository, detailing your suggestion. Here's what we'd like you to include in your enhancement suggestion:

- A clear and concise description of the enhancement.
- An explanation of why you think this enhancement would be beneficial to the project.
- Any other information that might help us understand your suggestion.

## Improving Documentation

If you've noticed that our documentation can be improved or expanded, we'd appreciate your input! Please open an issue in our GitHub repository, detailing your suggestions for the documentation. Here's what we'd like you to include in your documentation improvement suggestion:

- A clear and concise description of the documentation improvement.
- An explanation of why you think this improvement would be beneficial to the project.
- Any other information that might help us understand your suggestion.

## Pull Requests

If you're ready to start contributing code or documentation, please submit a pull request. Our team will review your submission as soon as possible. In order for your pull request to be approved, you'll need to follow our coding and documentation guidelines.

<!-- sek-g44:two-stage-template-release -->
## DCB template release protocol

DCB libraries and DCB templates use separate NuGet and Git tag series. In the release-integration PR, bump all
five `SekibanDcbTemplateVersion.props` authorities and the template README together, and add the four bilingual
release bodies for that version. Merge before the library tag. Gather dispatched checks on the merged commit,
write the host schema-3 `prepared` record, and require the prepared stage check on main before any tag.

With operator approval, publish the libraries first with annotated `dcb-vX.Y.Z`. The template workflow then
verifies all 26 library packages are publicly visible, the library Release has the exact assets and bilingual body,
both tags peel to the merged commit, and the five version authorities agree. Its packaged-consumer gate packs
the net9 carrier, installs it into an isolated `dotnet new` hive, generates all five net10 templates, restores,
builds, and runs the bundled tests. Only after the gates pass may annotated `dcbTemplates-vX.Y.Z` publish the
template package. Follow [the release runbook](dcb/tests/Sekiban.Dcb.TemplateValidation/RELEASE-RUNBOOK.md)
for record fields, mandatory preflight, operator approvals, retries and the new-version recovery rule.

The scheduled currency workflow compares stable `dcb-v*` and `dcbTemplates-v*` tags numerically. Pre-release and
unparseable tags are logged and excluded. A stale but still restorable template version is a release failure, not a
reason to skip the consumer gate.

`CONTRIBUTING.md` is intentionally outside the EN/JA documentation parity gate: it is the single contributor-facing
release procedure, while the paired materialized-view and storage-provider documents must remain semantically aligned.

Thank you once again for your interest in contributing to our project. We appreciate your effort and are excited to see what you bring to our project!
