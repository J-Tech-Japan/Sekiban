#!/usr/bin/env bash
# Prints the stale SekibanDcbVersion that run-packaged-consumer.sh writes into its currency mutant.
# The mutant must still restore and build so that currency validation is what rejects it.
#   x.y.z           -> 10.23.0 (an older published 10.x release; raise it when templates need a newer 10.x API).
#   x.y.z-previewNN -> x.y.z-preview(NN-1).stale: never published, ordered below the template version and above
#                      every earlier preview of x.y.z, so restore falls forward to the template version itself
#                      from the local candidate feed and, once it is public, from nuget.org.
set -euo pipefail
[[ $# == 1 ]] || { echo 'Usage: derive-stale-currency-version.sh <template-version>' >&2; exit 2; }
version="$1"
if [[ "$version" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ ]]; then
  echo '10.23.0'
elif [[ "$version" =~ ^((0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*))-preview(0[1-9]|[1-9][0-9])$ ]]; then
  printf '%s-preview%02d.stale\n' "${BASH_REMATCH[1]}" "$((10#${BASH_REMATCH[5]} - 1))"
else
  echo "Invalid template version: $version" >&2
  exit 2
fi
