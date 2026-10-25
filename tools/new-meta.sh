#!/usr/bin/env bash
# Write a Unity .meta file with a fresh GUID for each new asset path, so Unity never has to
# generate one on the PC. Create the file or folder first.
#   tools/new-meta.sh Assets/Scripts/Arcade Assets/Scripts/Arcade/Foo.cs
set -euo pipefail
for path in "$@"; do
    meta="$path.meta"
    if [ -e "$meta" ]; then echo "exists: $meta"; continue; fi
    guid=$(uuidgen | tr -d '-' | tr 'A-F' 'a-f')
    if [ -d "$path" ]; then
        printf 'fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' "$guid" > "$meta"
    else
        case "$path" in
            *.cs) printf 'fileFormatVersion: 2\nguid: %s\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' "$guid" > "$meta" ;;
            *.asmdef) printf 'fileFormatVersion: 2\nguid: %s\nAssemblyDefinitionImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' "$guid" > "$meta" ;;
            *) echo "unsupported asset type: $path" >&2; exit 1 ;;
        esac
    fi
    echo "wrote $meta"
done
