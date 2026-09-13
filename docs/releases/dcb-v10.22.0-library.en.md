# Sekiban DCB 10.22.0 library release body

This reviewed body is the library half of the DCB 10.22.0 two-stage release. The release is
prepared from the reviewed integration commit and may finalize only after the `prepared` and
`libraries-verified` states have been observed.

- Version: `10.22.0`.
- Package scope: exactly 26 DCB library packages, including the Azure Queue adapter.
- Orleans support: `Microsoft.Orleans.*` 10.3.1, with the published net9.0 and net10.0 asset
  groups.
- The PostgreSQL package retains its relational runtime closure; the Azure-free Orleans Core
  boundary remains Azure-free.

This file is a reviewed input for a future release workflow. It does not publish packages,
create a tag or release, close an issue, or claim that `libraries-verified` has been reached.
The template stage must use its separate reviewed body only after the library stage is verified.
A transient partial push may retry the unchanged immutable tag after public visibility is proven;
a source-changing recovery requires an operator-approved new version and a fresh review.

The delivered integration units are G74, G75, G76, G77, and G78. The default Orleans publisher
uses five attempts; size-gate and durable-recovery behavior remain opt-in boundaries. The whole
Orleans cluster is aligned on 10.3.1. PostgreSQL schema ownership remains explicit and this
release requires no data rewrite.
