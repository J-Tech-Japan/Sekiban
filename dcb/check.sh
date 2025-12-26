#!/usr/bin/env bash

# Purpose: Run the standard validation cycle for the Sekiban.Dcb solution ONCE with clear status.
# Steps:
#  1) dotnet format (first verify, then apply if needed) -> report changed files
#  2) dotnet build -> report success/failure
#  3) Print a compact summary at the end

set -uo pipefail # Deliberately avoid -e to allow summary printing even on failures

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEFAULT_SOLUTION_REL="Sekiban.Dcb.slnx"

# Options parsing: --no-color | --color, and optional solution path
COLOR_MODE="auto" # auto|always|never
SOLUTION_REL_PATH=""
for arg in "$@"; do
  case "$arg" in
    --no-color) COLOR_MODE="never" ;;
    --color)    COLOR_MODE="always" ;;
    *)          # first non-flag arg is treated as solution path
                if [[ -z "$SOLUTION_REL_PATH" ]]; then SOLUTION_REL_PATH="$arg"; fi ;;
  esac
done
[[ -z "$SOLUTION_REL_PATH" ]] && SOLUTION_REL_PATH="$DEFAULT_SOLUTION_REL"

# ANSI colors (fallback to no color if not a terminal or disabled)
ENABLE_COLOR=0
case "$COLOR_MODE" in
  always) ENABLE_COLOR=1 ;;
  never)  ENABLE_COLOR=0 ;;
  auto)
    if { [[ -t 1 ]] || [[ -t 2 ]] ; } && [[ -z "${NO_COLOR:-}" ]] && [[ "${TERM:-}" != "dumb" ]]; then
      ENABLE_COLOR=1
    fi
    ;;
esac

if [[ $ENABLE_COLOR -eq 1 ]]; then
  C_RESET=$'\033[0m'; C_GREEN=$'\033[32m'; C_RED=$'\033[31m'; C_YELLOW=$'\033[33m'; C_BLUE=$'\033[34m'
else
  C_RESET=""; C_GREEN=""; C_RED=""; C_YELLOW=""; C_BLUE=""
fi

ICON_OK="[OK]"; ICON_FAIL="[FAIL]"; ICON_FIX="[FIX]"; ICON_INFO="[INFO]"
SOLUTION_PATH="$ROOT_DIR/$SOLUTION_REL_PATH"

if [[ ! -f "$SOLUTION_PATH" ]]; then
  echo "[check.sh] Solution not found: $SOLUTION_PATH" >&2
  echo "[check.sh] Usage: $0 [relative/path/to/solution.slnx]" >&2
  exit 1
fi

printf "[check.sh] Working directory: %s\n" "$ROOT_DIR"
printf "[check.sh] Using solution: %s\n" "$SOLUTION_REL_PATH"

# Helper to print a section header
section() {
  printf "\n%s[check.sh]%s %s\n" "$C_BLUE" "$C_RESET" "$1" >&2
}

# Helper to run a command (show it) and return its status
run_cmd() {
  printf "\n[check.sh] %s\n" "$*" >&2
  "$@"; return $?
}

# Detect if we're in a git repo
IN_GIT=0
if command -v git >/dev/null 2>&1; then
  if git -C "$ROOT_DIR" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    IN_GIT=1
  fi
fi

# Temp dir (macOS friendly)
TMP_DIR="$(mktemp -d -t dcb-check-XXXXXX 2>/dev/null || mktemp -d 2>/dev/null || echo "$ROOT_DIR")"
cleanup() { [[ -n "$TMP_DIR" && -d "$TMP_DIR" ]] && rm -rf "$TMP_DIR" 2>/dev/null || true; }
trap cleanup EXIT

FORMAT_STATUS=""  # OK | FIXED | FAILED
FORMAT_CHANGED_COUNT=0

section "1) dotnet format: verify"
VERIFY_RC=0
run_cmd dotnet format "$SOLUTION_PATH" --verify-no-changes || VERIFY_RC=$?

if [[ $VERIFY_RC -eq 0 ]]; then
  FORMAT_STATUS="OK"
  printf "%s %sFormat%s: No changes needed\n" "$ICON_OK" "$C_GREEN" "$C_RESET"
else
  # There are format issues or an error occurred. Try to apply formatting.
  # Capture modified files before formatting (only if in git).
  if [[ $IN_GIT -eq 1 ]]; then
    git -C "$ROOT_DIR" ls-files -m >"$TMP_DIR/before.txt" 2>/dev/null || :
  fi

  section "1) dotnet format: apply"
  APPLY_RC=0
  run_cmd dotnet format "$SOLUTION_PATH" || APPLY_RC=$?

  if [[ $APPLY_RC -ne 0 ]]; then
    FORMAT_STATUS="FAILED"
    printf "%s %sFormat%s: Failed to apply fixes (exit %d)\n" "$ICON_FAIL" "$C_RED" "$C_RESET" "$APPLY_RC"
  else
    if [[ $IN_GIT -eq 1 ]]; then
      git -C "$ROOT_DIR" ls-files -m >"$TMP_DIR/after.txt" 2>/dev/null || :
      # Compute newly modified files due to format
      sort "$TMP_DIR/before.txt" >"$TMP_DIR/before.sorted" 2>/dev/null || :
      sort "$TMP_DIR/after.txt"  >"$TMP_DIR/after.sorted"  2>/dev/null || :
      comm -13 "$TMP_DIR/before.sorted" "$TMP_DIR/after.sorted" >"$TMP_DIR/changed.txt" 2>/dev/null || :
      FORMAT_CHANGED_COUNT=$(wc -l <"$TMP_DIR/changed.txt" 2>/dev/null | tr -d ' ' || echo 0)
      if [[ "$FORMAT_CHANGED_COUNT" -gt 0 ]]; then
        FORMAT_STATUS="FIXED"
        printf "%s %sFormat%s: Applied fixes to %d file(s)\n" "$ICON_FIX" "$C_YELLOW" "$C_RESET" "$FORMAT_CHANGED_COUNT"
        # Show up to 20 files for brevity
        awk 'NR<=20{print "  - "$0} NR==21{print "  ..."; exit}' "$TMP_DIR/changed.txt" 2>/dev/null || :
      else
        FORMAT_STATUS="OK"
        printf "%s %sFormat%s: No files changed (already formatted or no diff available)\n" "$ICON_OK" "$C_GREEN" "$C_RESET"
      fi
    else
      FORMAT_STATUS="FIXED"
      printf "%s %sFormat%s: Fixes applied (git not available to list files)\n" "$ICON_FIX" "$C_YELLOW" "$C_RESET"
    fi
  fi
fi

section "2) dotnet build (warnings as errors)"
BUILD_RC=0
BUILD_LOG="$TMP_DIR/build.log"
run_cmd dotnet build "$SOLUTION_PATH" --no-incremental -warnaserror 2>&1 | tee "$BUILD_LOG" || BUILD_RC=${PIPESTATUS[0]}
if [[ $BUILD_RC -eq 0 ]]; then
  printf "%s %sBuild%s: Succeeded\n" "$ICON_OK" "$C_GREEN" "$C_RESET"
else
  printf "%s %sBuild%s: Failed (exit %d)\n" "$ICON_FAIL" "$C_RED" "$C_RESET" "$BUILD_RC"
fi

# Extract errors and warnings from build log
BUILD_ERRORS=""
if [[ -f "$BUILD_LOG" ]]; then
  BUILD_ERRORS=$(grep -E ': (error|warning) [A-Z]+[0-9]+:' "$BUILD_LOG" 2>/dev/null || true)
fi

section "Summary"
summ_line() {
  local label="$1"; local status="$2"; local rc="$3"
  case "$status" in
    OK)     printf "  %s %s: %sOK%s\n"     "$label" "$ICON_OK"   "$C_GREEN" "$C_RESET" ;;
    FIXED)  printf "  %s %s: %sFIXED%s\n"  "$label" "$ICON_FIX"  "$C_YELLOW" "$C_RESET" ;;
    FAILED) printf "  %s %s: %sFAILED%s\n" "$label" "$ICON_FAIL" "$C_RED" "$C_RESET" ;;
    *)      printf "  %s %s: %s (rc=%s)\n" "$label" "$ICON_INFO" "$status" "$rc" ;;
  esac
}

summ_line "Format" "$FORMAT_STATUS" "-"
summ_line "Build " "$([[ $BUILD_RC -eq 0 ]] && echo OK || echo FAILED)" "$BUILD_RC"

# Report build errors/warnings if any
if [[ -n "$BUILD_ERRORS" ]]; then
  section "Build Errors/Warnings (could not auto-fix)"
  printf "%s\n" "$BUILD_ERRORS" | head -50
  ERROR_COUNT=$(printf "%s\n" "$BUILD_ERRORS" | wc -l | tr -d ' ')
  if [[ "$ERROR_COUNT" -gt 50 ]]; then
    printf "  ... and %d more\n" $((ERROR_COUNT - 50))
  fi
fi

# Exit with non-zero if format apply or build failed
if [[ "$FORMAT_STATUS" == "FAILED" || $BUILD_RC -ne 0 ]]; then
  exit 1
fi

printf "\n[check.sh] All checks completed.\n"
