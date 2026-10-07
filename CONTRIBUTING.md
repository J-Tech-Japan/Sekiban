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

DCB libraries and templates release separately. The library release-integration PR moves all five
`SekibanDcbTemplateVersion.props` authorities and the template README to V and adds the EN/JA library
notes. PR CI requires that pair; template notes may be absent, but a half pair fails. Collect the four
dispatched library checks, write the library schema-3 record and require the library prepared stage check.
With operator approval, publish annotated `dcb-vV` first.

After V is public, audit and adjust templates for best practice on released V and add template notes in
another PR. Templates may release from that later commit while the authorities still read V; moving to
V+1 ends V's template window. Dispatch template validation with `public_libraries=true`, require every
job green and the nuget.org consumer successful, then write the separate template record using exactly
`templatePublicConsumer`. The host variable must point at a commit containing both records, keeping
the library record unchanged. Require the template prepared stage check before operator approval and
annotated `dcbTemplates-vV` tagging.

The template gates retain record/HEAD/main containment, template tag identity, authority parity,
26 public library versions, exact non-draft library Release assets and body, public consumer,
chronology, absence/retry identity, visibility and equality. The annotated library tag's verified
live/local commit must be an ancestor of the template commit; its SHA supplies the library body files.
The packaged consumer installs the net9 carrier in an isolated hive, generates all five net10 templates,
restores, builds and tests. Follow [the release runbook](dcb/tests/Sekiban.Dcb.TemplateValidation/RELEASE-RUNBOOK.md)
for both records, complete checks, the 10.23.1 variable sequence and recovery. The new-version stop rule
applies to the artifact being fixed; unpublished templates normally change after library publication.

The scheduled currency check compares stable tags numerically, excluding logged pre-release and
unparseable tags. Library ahead is a notice and success; template ahead fails. The operator chooses
how long templates may lag. Every consumer gate remains required when it applies.

`CONTRIBUTING.md` is intentionally outside the EN/JA documentation parity gate: it is the single contributor-facing
release procedure, while the paired materialized-view and storage-provider documents must remain semantically aligned.

Thank you once again for your interest in contributing to our project. We appreciate your effort and are excited to see what you bring to our project!
