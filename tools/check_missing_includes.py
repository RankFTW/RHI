import os
import re

def strip_comments(text):
    text = re.sub(r'/\*.*?\*/', '', text, flags=re.DOTALL)
    text = re.sub(r'//.*', '', text)
    return text

def check_missing_includes():
    base_dir = os.path.expandvars(r"%LocalAppData%\RHI\reshade")
    shaders_dir = os.path.join(base_dir, "Shaders")

    if not os.path.exists(shaders_dir):
        print(f"Error: Could not find 'Shaders' folder at {shaders_dir}")
        return

    # 1. Gather EVERY directory in the repository tree to use as a potential search path
    search_paths = [base_dir, shaders_dir]
    for root, dirs, _ in os.walk(base_dir):
        for d in dirs:
            search_paths.append(os.path.abspath(os.path.join(root, d)))

    search_paths = [p.replace('\\', '/').lower() for p in set(search_paths)]

    # 2. Map every single physical file on disk by its lowercase absolute path
    file_universe = set()
    for root, _, files in os.walk(base_dir):
        for file in files:
            abs_path = os.path.abspath(os.path.join(root, file)).replace('\\', '/').lower()
            file_universe.add(abs_path)

    # 3. Regex to match #include statements
    include_regex = re.compile(r'#include\s+["<]([^">]+)[">]')
    missing_map = {}

    # 4. Scan all shader and header files
    for root, _, files in os.walk(shaders_dir):
        for file in files:
            if file.endswith('.fx') or file.endswith('.fxh'):
                file_path = os.path.abspath(os.path.join(root, file)).replace('\\', '/')
                current_shader_dir = os.path.dirname(file_path).replace('\\', '/').lower()
                relative_shader_path = os.path.relpath(file_path, base_dir)

                try:
                    with open(file_path, 'r', encoding='utf-8', errors='ignore') as f:
                        content = f.read()

                    clean_content = strip_comments(content)

                    for line in clean_content.splitlines():
                        match = include_regex.search(line)
                        if match:
                            raw_include = match.group(1).replace('\\', '/')

                            clean_include = raw_include
                            if clean_include.startswith('./'):
                                clean_include = clean_include[2:]
                            elif clean_include.startswith('/'):
                                clean_include = clean_include[1:]

                            resolved = False

                            # Rule A: Check relative to the current file's folder
                            test_path = os.path.abspath(os.path.join(current_shader_dir, clean_include)).replace('\\', '/').lower()
                            if test_path in file_universe:
                                resolved = True

                            # Rule B: Check against every subfolder in the tree
                            if not resolved:
                                for path_dir in search_paths:
                                    test_path = os.path.abspath(os.path.join(path_dir, clean_include)).replace('\\', '/').lower()
                                    if test_path in file_universe:
                                        resolved = True
                                        break

                            if not resolved:
                                if raw_include not in missing_map:
                                    missing_map[raw_include] = []
                                missing_map[raw_include].append(relative_shader_path)

                except Exception as e:
                    print(f"Could not read {relative_shader_path}: {e}")

    # 5. Print results
    print("\n=== RESHADE MISSING INCLUDES REPORT ===")
    if not missing_map:
        print("Perfect! All active #include files resolved successfully.")
    else:
        print(f"Found {len(missing_map)} unique missing include file(s):\n")
        for missing_file, shaders in sorted(missing_map.items()):
            print(f"  {missing_file}")
            print("   Required by:")
            for shader in sorted(set(shaders)):
                print(f"     - {shader}")
            print()

if __name__ == "__main__":
    check_missing_includes()
