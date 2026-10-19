"""Fail-closed published catalog identity/payload retention gate."""
import copy
import json
import os
import re
import subprocess
import sys
from urllib.parse import urlparse

import yaml


class UniqueKeyLoader(yaml.SafeLoader):
    pass


def unique_mapping(loader, node):
    result = {}
    for key_node, value_node in node.value:
        key = loader.construct_object(key_node)
        if key in result:
            raise ValueError(f"duplicate YAML key: {key}")
        result[key] = loader.construct_object(value_node)
    return result


UniqueKeyLoader.add_constructor(
    yaml.resolver.BaseResolver.DEFAULT_MAPPING_TAG, unique_mapping
)


def git(*args):
    return subprocess.check_output(["git", *args], stderr=subprocess.PIPE)


def snapshot(revision):
    result = {}
    # Inspect tree modes too: git show would otherwise accept symlink contents.
    for entry in git("ls-tree", "-rz", revision, "--", "stock-modules-manifests/").split(b"\0"):
        if not entry:
            continue
        metadata, path_bytes = entry.split(b"\t", 1)
        path = os.fsdecode(path_bytes)
        if not path.endswith(".yaml"):
            continue
        mode, kind, oid = metadata.split()
        if mode not in (b"100644", b"100755") or kind != b"blob":
            raise ValueError(f"{path}: release manifest must be a regular file")
        if path.count("/") != 1:
            raise ValueError(f"{path}: release manifests must be in the catalog root")
        result[path] = yaml.load(git("cat-file", "blob", oid.decode()), Loader=UniqueKeyLoader)
    return result


def identities(files, validate_pins):
    result = {}
    for path, manifest in files.items():
        if not isinstance(manifest, dict):
            raise ValueError(f"{path}: expected a YAML mapping")
        name, version = manifest.get("name"), manifest.get("version")
        if not isinstance(name, str) or not re.fullmatch(r"[a-zA-Z0-9._-]{1,256}", name):
            raise ValueError(f"{path}: invalid name")
        if not isinstance(version, str) or len(version) > 64 or not re.fullmatch(
            r"v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)", version
        ):
            raise ValueError(f"{path}: only stable major.minor.patch releases are supported")
        key = (name.casefold(), version.casefold())
        if key in result:
            raise ValueError(f"{path}: duplicate release {name}@{version} (also {result[key][0]})")
        if validate_pins:
            source = manifest.get("source")
            if not isinstance(source, dict) or source.get("kind") != "git":
                raise ValueError(f"{path}: source.kind must be git")
            if not isinstance(source.get("commit_sha"), str) or not re.fullmatch(
                r"[0-9a-fA-F]{40}", source["commit_sha"]
            ):
                raise ValueError(f"{path}: source.commit_sha must be a full Git commit SHA")
            if not isinstance(source.get("ref"), str) or not re.fullmatch(
                r"[a-zA-Z0-9_./-]{1,256}", source["ref"]
            ):
                raise ValueError(f"{path}: source.ref must identify a release tag")
            locator = source.get("locator")
            if not isinstance(locator, str) or urlparse(locator).scheme != "https" or not urlparse(locator).netloc:
                raise ValueError(f"{path}: source.locator must be an absolute HTTPS URL")
            parameters = manifest.get("parameters", [])
            if not isinstance(parameters, list):
                raise ValueError(f"{path}: parameters must be a list")
            names = set()
            for parameter in parameters:
                if not isinstance(parameter, dict) or not isinstance(parameter.get("name"), str) or not re.fullmatch(
                    r"[a-zA-Z0-9_]+", parameter["name"]
                ):
                    raise ValueError(f"{path}: parameter names must contain only [a-zA-Z0-9_]")
                name = parameter["name"]
                if name in names:
                    raise ValueError(f"{path}: duplicate parameter '{name}'")
                names.add(name)
                if parameter.get("type") not in ("String", "Number", "Bool"):
                    raise ValueError(f"{path}: parameter '{name}' has unsupported type")
                if not isinstance(parameter.get("integer", False), bool) or (
                    parameter.get("integer") and parameter["type"] != "Number"
                ):
                    raise ValueError(f"{path}: integer constraints require Number parameters")
            conflicts = manifest.get("parameter_conflicts", [])
            if not isinstance(conflicts, list):
                raise ValueError(f"{path}: parameter_conflicts must be a list")
            for conflict in conflicts:
                if not isinstance(conflict, dict) or conflict.get("parameter") not in names or conflict.get("when_parameter") not in names:
                    raise ValueError(f"{path}: parameter_conflicts must reference declared parameters")
                if not all(isinstance(conflict.get(field), str) for field in ("value", "when_value", "message")) or not conflict["message"].strip():
                    raise ValueError(f"{path}: parameter_conflicts require string values and a nonempty message")
        deprecated = manifest.get("deprecated", False)
        if not isinstance(deprecated, bool):
            raise ValueError(f"{path}: deprecated must be a YAML boolean")
        if deprecated and not str(manifest.get("deprecation_reason") or "").strip():
            raise ValueError(f"{path}: deprecated releases require a deprecation_reason")
        result[key] = (path, manifest)
    return result


def payload(manifest):
    result = copy.deepcopy(manifest)
    result.pop("deprecated", None)
    result.pop("deprecation_reason", None)
    return json.dumps(result, sort_keys=True, ensure_ascii=False, allow_nan=False)


def check(base_files, head_files):
    base = identities(base_files, validate_pins=False)
    head = identities(head_files, validate_pins=True)
    for identity, (old_path, old) in base.items():
        if identity not in head:
            raise ValueError(f"{old_path}: published releases must be retained; deprecate instead of deleting")
        new_path, new = head[identity]
        if payload(old) != payload(new):
            raise ValueError(f"{new_path}: immutable release payload/identity changed; publish a new version")
        if old.get("deprecated", False) and not new.get("deprecated", False):
            raise ValueError(f"{new_path}: deprecation is one-way; publish a new release instead of reactivating")
        if not new.get("deprecated", False) and old.get("deprecation_reason") != new.get("deprecation_reason"):
            raise ValueError(f"{new_path}: deprecation_reason can only change on a deprecated release")


def main():
    try:
        if len(sys.argv) != 3:
            raise ValueError("usage: manifest_immutability.py BASE_SHA HEAD_SHA")
        merge_base = git("merge-base", sys.argv[1], sys.argv[2]).decode().strip()
        check(snapshot(merge_base), snapshot(sys.argv[2]))
    except (ValueError, TypeError, yaml.YAMLError, subprocess.CalledProcessError, OSError) as error:
        print(f"manifest-immutability: {error}; failing closed.", file=sys.stderr)
        return 1
    print("manifest-immutability: all published identities retained and payloads unchanged.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
