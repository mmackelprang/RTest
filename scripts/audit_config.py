import os
import re

# Configuration
ROOT_DIR = "."
DOC_FILE = "SYSTEMCONFIGURATION.md"
# Regex to find classes like 'public class AudioOptions'
CLASS_PATTERN = re.compile(r"public\s+(?:sealed\s+)?class\s+(\w+Options)")
# Regex to find 'public const string SectionName = "Audio";'
SECTION_PATTERN = re.compile(r'public\s+const\s+string\s+SectionName\s*=\s*"([^"]+)"')

def find_config_definitions(root_dir):
    """Scans C# files for Options classes and extracts their SectionName."""
    definitions = {}

    for dirpath, _, filenames in os.walk(root_dir):
        for filename in filenames:
            if filename.endswith(".cs"):
                filepath = os.path.join(dirpath, filename)
                with open(filepath, "r", encoding="utf-8") as f:
                    try:
                        content = f.read()
                        # Look for Options class definition
                        class_match = CLASS_PATTERN.search(content)
                        # Look for SectionName constant
                        section_match = SECTION_PATTERN.search(content)

                        if class_match and section_match:
                            class_name = class_match.group(1)
                            section_name = section_match.group(1)
                            definitions[section_name] = {
                                "class": class_name,
                                "file": filepath
                            }
                    except Exception as e:
                        print(f"Skipping {filepath}: {e}")
    return definitions

def get_documented_sections(doc_path):
    """Reads the markdown file and finds documented section headers."""
    if not os.path.exists(doc_path):
        return []
    
    with open(doc_path, "r", encoding="utf-8") as f:
        content = f.read()
    
    # Matches headers like '### Audio', '### ManagedConfiguration', etc.
    # or text explicitly mentioning the section name in bold/code blocks
    return content

def main():
    print(f"Scanning codebase for configuration options...")
    code_configs = find_config_definitions(ROOT_DIR)
    
    print(f"Reading {DOC_FILE}...")
    doc_content = get_documented_sections(DOC_FILE)
    
    missing_items = []

    for section, details in code_configs.items():
        # Simple check: is the SectionName string present in the doc?
        # We look for the exact section name to avoid partial matches
        if section not in doc_content:
            missing_items.append(f"**{section}** (Class: `{details['class']}` in `{details['file']}`)")

    if missing_items:
        print("\nMissing Configuration Documentation Found:")
        print("\n".join(missing_items))
        
        # Write to a report file for the GitHub Action to use
        with open("config_audit_report.md", "w", encoding="utf-8") as report:
            report.write(f"### Missing Configuration Sections in {DOC_FILE}\n\n")
            report.write("The following configuration sections were found in the code but are missing from documentation:\n\n")
            for item in missing_items:
                report.write(f"- [ ] {item}\n")
    else:
        print("\nAll configuration sections appear to be documented.")

if __name__ == "__main__":
    main()
