# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Language Guidelines

- This repository is OSS that people from many languages read it. please write all comment or document in English, unless we ask

## ⚠️ CRITICAL RULE FOR TYPESCRIPT DEVELOPMENT

**NEVER CREATE SIMPLIFIED IMPLEMENTATIONS THAT SKIP PROPER ACTOR IMPLEMENTATION.**

The Sekiban TypeScript implementation is under development. When you encounter missing features:
- DO NOT create in-memory workarounds
- DO NOT bypass the actor system
- DO NOT create "temporary" solutions
- DO ask about the proper approach
- DO help implement features properly

Every simplified implementation creates garbage code that must be completely rewritten.

[... rest of the existing content remains unchanged ...]