# Sekiban DCB Templates 10.22.0 release body

This is the separate reviewed template body for the DCB 10.22.0 release train. The `prepared`
template stage starts only after the same peeled integration commit has reached `libraries-verified` and
then `template-tagged/incomplete`.

- Template package: `Sekiban.Dcb.Templates` version `10.22.0`.
- The five generated authorities and README use DCB 10.22.0 and retain Orleans 10.3.1.
- The generated net10 projects, net9 carrier, PostgreSQL consumers, and Azure-free Orleans Core
  dependency boundary are validated from an isolated local feed before publication.

This body is a reviewed input for the `artifacts-verified` stage. It does not publish the
template, create a tag or release, close an issue, or claim that the stage has completed.
Transient publication failures may retry the unchanged template tag; source-changing recovery
requires an operator-approved new version and a fresh review.
