/**
 * Runtime-only exports that require actual storage provider dependencies
 * These are separated to avoid build-time dependency issues
 */

export { createStorageProvider } from '../providers/storage'