import copy
import unittest
from unittest.mock import patch

import manifest_immutability as gate


def manifest(version="1.0.0"):
    return {
        "name": "module",
        "version": version,
        "source": {"kind": "git", "locator": "https://example.com/modules.git",
                   "ref": f"module/v{version}", "commit_sha": "a" * 40},
        "parameters": [],
        "deprecated": False,
        "deprecation_reason": "",
    }


class ReleaseRetentionTests(unittest.TestCase):
    def setUp(self):
        self.base = {"stock-modules-manifests/module.yaml": manifest()}

    def test_add_new_version_and_identical_rename(self):
        gate.check(self.base, {
            "stock-modules-manifests/renamed.yaml": manifest(),
            "stock-modules-manifests/new.yaml": manifest("1.1.0"),
        })

    def test_duplicate_identity_even_when_earlier_filename_or_case_differs(self):
        for name in ("module", "MODULE"):
            duplicate = manifest()
            duplicate["name"] = name
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, "duplicate release"):
                gate.check(self.base, {**self.base, "stock-modules-manifests/aaa.yaml": duplicate})

    def test_removal_is_rejected_even_when_deprecated(self):
        for deprecated in (False, True):
            old = manifest()
            old.update(deprecated=deprecated, deprecation_reason="superseded" if deprecated else "")
            with self.subTest(deprecated=deprecated), self.assertRaisesRegex(ValueError, "retained"):
                gate.check({"old.yaml": old}, {})

    def test_delete_readd_or_rename_cannot_change_payload(self):
        for field, value in (("parameters", [{"name": "injected"}]), ("description", "changed"),
                             ("name", "different"), ("version", "2.0.0")):
            changed = manifest()
            changed[field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                gate.check(self.base, {"stock-modules-manifests/readded.yaml": changed})
        changed = manifest()
        changed["source"]["commit_sha"] = "b" * 40
        with self.assertRaisesRegex(ValueError, "immutable"):
            gate.check(self.base, {"renamed.yaml": changed})

    def test_only_deprecation_metadata_may_change(self):
        changed = manifest()
        changed.update(deprecated=True, deprecation_reason="use 2.0.0")
        gate.check(self.base, {"renamed.yaml": changed})
        old = copy.deepcopy(changed)
        changed["deprecation_reason"] = "clarified successor"
        gate.check({"old.yaml": old}, {"new.yaml": changed})
        with self.assertRaisesRegex(ValueError, "one-way"):
            gate.check({"old.yaml": old}, self.base)
        changed["parameters"] = [{"name": "changed", "type": "String"}]
        with self.assertRaisesRegex(ValueError, "immutable"):
            gate.check(self.base, {"new.yaml": changed})

    def test_missing_reason_or_invalid_pin_fails(self):
        for sha in (None, "abc123", "z" * 40):
            changed = manifest()
            changed["source"]["commit_sha"] = sha
            with self.subTest(sha=sha), self.assertRaisesRegex(ValueError, "commit_sha"):
                gate.check({}, {"new.yaml": changed})
        changed = manifest()
        changed["deprecated"] = True
        with self.assertRaisesRegex(ValueError, "deprecation_reason"):
            gate.check(self.base, {"new.yaml": changed})

    def test_git_failure_and_duplicate_yaml_keys_fail_closed(self):
        with patch.object(gate, "git", side_effect=OSError("git failed")), \
             patch.object(gate.sys, "argv", ["gate", "base", "head"]):
            self.assertEqual(1, gate.main())
        with self.assertRaisesRegex(ValueError, "duplicate YAML key"):
            gate.yaml.load("name: one\nname: two", Loader=gate.UniqueKeyLoader)

    def test_yaml_scalar_types_cannot_change_behind_equal_python_values(self):
        old = manifest()
        old["parameters"] = [{"name": "input", "type": "String", "default": True}]
        changed = copy.deepcopy(old)
        changed["parameters"][0]["default"] = 1
        with self.assertRaisesRegex(ValueError, "immutable"):
            gate.check({"old.yaml": old}, {"new.yaml": changed})

    def test_tree_snapshot_rejects_symlink_and_loads_real_yaml(self):
        entry = b"120000 blob " + b"a" * 40 + b"\tstock-modules-manifests/release.yaml\0"
        with patch.object(gate, "git", return_value=entry), self.assertRaisesRegex(ValueError, "regular file"):
            gate.snapshot("head")
        entry = entry.replace(b"120000", b"100644")
        with patch.object(gate, "git", side_effect=[entry, gate.yaml.safe_dump(manifest()).encode()]):
            self.assertEqual({"stock-modules-manifests/release.yaml": manifest()}, gate.snapshot("head"))

    def test_prereleases_are_rejected_not_misordered(self):
        with self.assertRaisesRegex(ValueError, "stable"):
            gate.check({}, {"new.yaml": manifest("2.0.0-rc.1")})


if __name__ == "__main__":
    unittest.main()
