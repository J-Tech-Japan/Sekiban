#!/usr/bin/env python3
import os
import re
import sys

def fix_imports_in_file(filepath):
    """Fix import statements in a TypeScript file."""
    try:
        with open(filepath, 'r') as f:
            content = f.read()
    except:
        return
    
    original_content = content
    
    # Fix patterns
    patterns = [
        # Fix '../events' to '../events/index.js'
        (r"from '\.\./(events|aggregates|commands|queries|documents|result|storage|executors|serialization|utils|validation)'(?!\.js)", r"from '../\1/index.js'"),
        # Fix './events' to './events/index.js'
        (r"from '\./(events|aggregates|commands|queries|documents|result|storage|executors|serialization|utils|validation)'(?!\.js)", r"from './\1/index.js'"),
        # Fix specific wrong paths
        (r"from '\.\./partition-keys/partition-keys\.js'", r"from '../documents/partition-keys.js'"),
        (r"from '\.\./errors/sekiban-error\.js'", r"from '../result/errors.js'"),
        # Fix bare imports that need .js
        (r"from '(\.\.?/[^']+)'(?<!\.js)(?<!\.json)(?<!\.css)'", r"from '\1.js'"),
        # Fix double .js.js
        (r"\.js\.js'", r".js'"),
        # Fix index.js.js
        (r"/index\.js\.js'", r"/index.js'"),
    ]
    
    for pattern, replacement in patterns:
        content = re.sub(pattern, replacement, content)
    
    # Only write if changed
    if content != original_content:
        with open(filepath, 'w') as f:
            f.write(content)
        print(f"Fixed: {filepath}")

def main():
    base_dir = "/Users/tomohisa/dev/GitHub/Sekiban-ts/ts/src/packages/core/src"
    
    for root, dirs, files in os.walk(base_dir):
        for file in files:
            if file.endswith('.ts') and not file.endswith('.test.ts') and not file.endswith('.d.ts'):
                filepath = os.path.join(root, file)
                fix_imports_in_file(filepath)

if __name__ == "__main__":
    main()