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

DCB libraries and DCB templates are intentionally separate NuGet and Git tag series. Release a library version first
with `dcb-vX.Y.Z`; do not publish a matching template merely because the source version was edited. After the library
publish, use a bounded retry with a timeout to confirm that all 17 template package IDs resolve from nuget.org. Then
move all five `SekibanDcbTemplateVersion.props` authorities to the published version and run the packaged-consumer
gate: pack the net9 carrier, install it into an isolated `dotnet new` hive, generate all five net10 templates, restore
only from nuget.org using an isolated package cache, build, and run the bundled tests. Only after that gate is green
may `dcbTemplates-vX.Y.Z` publish the template package.

The scheduled currency workflow compares stable `dcb-v*` and `dcbTemplates-v*` tags numerically. Pre-release and
unparseable tags are logged and excluded. A stale but still restorable template version is a release failure, not a
reason to skip the consumer gate.

`CONTRIBUTING.md` is intentionally outside the EN/JA documentation parity gate: it is the single contributor-facing
release procedure, while the paired materialized-view and storage-provider documents must remain semantically aligned.

Thank you once again for your interest in contributing to our project. We appreciate your effort and are excited to see what you bring to our project!
